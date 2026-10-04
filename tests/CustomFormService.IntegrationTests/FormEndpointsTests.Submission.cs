using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task R8_submit_all_types_returns_persisted_sorted_receipt_without_changing_definition(bool ownerSubmits)
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R8 Owner");
        var respondent = ownerSubmits ? owner : await CreateMemberAsync("R8 Respondent");
        var form = await CreateR8FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(form.Id);
        clock.Now = clock.Now.AddTicks(7); // PostgreSQL stores microseconds, Receipt must use that actual value.
        clock.Reset();
        var submit = await R8SubmitAsync(form, respondent.Cookie, R8AllAnswers(form));
        Assert.AreEqual(HttpStatusCode.Created, submit.StatusCode, await submit.Content.ReadAsStringAsync());
        Assert.AreEqual(1, clock.Calls);
        using var receipt = await ReadJsonAsync(submit);
        CollectionAssert.AreEquivalent(new[] { "id", "formId", "submittedAt", "answers" },
            receipt.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(form.Id, receipt.RootElement.GetProperty("formId").GetGuid());
        var answers = receipt.RootElement.GetProperty("answers").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(form.Questions, answers.Select(answer => answer.GetProperty("questionId").GetGuid()).ToArray());
        foreach (var answer in answers)
            CollectionAssert.AreEquivalent(new[] { "questionId", "value" }, answer.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("Hello 😀", answers[0].GetProperty("value").GetString());
        Assert.AreEqual("1", answers[1].GetProperty("value").GetRawText(), "JSONB may change the original 1e0 token.");
        Assert.AreEqual(form.Options[0].ToString("D"), answers[2].GetProperty("value").GetString());
        CollectionAssert.AreEqual(form.Options.Skip(1).Reverse().Select(id => id.ToString("D")).ToArray(),
            answers[3].GetProperty("value").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.AreEqual(5, answers[4].GetProperty("value").GetInt32());
        var responseId = receipt.RootElement.GetProperty("id").GetGuid();
        Assert.AreEqual(respondent.Id, await ScalarAsync<Guid>("SELECT user_id FROM responses WHERE id=@id", new NpgsqlParameter("id", responseId)));
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(form.Id));
        await AssertR8PersistedReceiptAsync(receipt.RootElement);
    }

    [TestMethod]
    public async Task R8_multiple_preserves_history_and_omits_optional_answers_from_new_response()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R8 Multiple");
        var form = await CreateR8FormAsync(owner.Cookie, mode: "multiple");
        using var first = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form)));
        clock.Now = clock.Now.AddSeconds(1);
        var second = await R8SubmitAsync(form, owner.Cookie,
            $$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":"  "},{"questionId":"{{form.Questions[3]}}","value":[]}]}""");
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());
        using var latest = await ReadJsonAsync(second);
        Assert.AreEqual(0, latest.RootElement.GetProperty("answers").GetArrayLength());
        Assert.AreNotEqual(first.RootElement.GetProperty("id").GetGuid(), latest.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(2L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(5L, await R8AnswerCountAsync(form.Id));
        await AssertR8PersistedReceiptAsync(first.RootElement);
        await AssertR8PersistedReceiptAsync(latest.RootElement);
    }

    [TestMethod]
    [DataRow("0.123456789012345678901234567890123456789")]
    [DataRow("1e30")]
    [DataRow("1e-30")]
    public async Task R8_number_round_trip_uses_actual_EF_JSONB_value_without_precision_loss(string token)
    {
        var owner = await CreateMemberAsync("R8 Exact Number");
        var form = await CreateR8FormAsync(owner.Cookie);
        var submit = await R8SubmitAsync(form, owner.Cookie,
            $$"""{"answers":[{"questionId":"{{form.Questions[1]}}","value":{{token}}}]}""");
        Assert.AreEqual(HttpStatusCode.Created, submit.StatusCode, await submit.Content.ReadAsStringAsync());
        using var receipt = await ReadJsonAsync(submit);
        await AssertR8PersistedReceiptAsync(receipt.RootElement);
        Assert.IsTrue(await ScalarAsync<bool>("SELECT value = CAST(@token AS jsonb) FROM answers WHERE response_id=@id",
            new NpgsqlParameter("token", token), new NpgsqlParameter("id", receipt.RootElement.GetProperty("id").GetGuid())));
    }

    [TestMethod]
    [DataRow("draft")]
    [DataRow("closed")]
    public async Task R8_non_published_and_missing_answers_returns_form_not_submittable(string status)
    {
        var owner = await CreateMemberAsync("R8 Priority");
        var other = await CreateMemberAsync("R8 Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        await ExecuteAsync("UPDATE forms SET status=CAST(@status AS form_status) WHERE id=@id",
            new NpgsqlParameter("status", status), new NpgsqlParameter("id", form.Id));
        await AssertErrorAsync(await R8SubmitAsync(form, other.Cookie, "{}"), HttpStatusCode.Conflict, "FORM_NOT_SUBMITTABLE");
        Assert.AreEqual(0L, await R8ResponseCountAsync(form.Id));
    }

    [TestMethod]
    public async Task R8_transport_auth_and_resource_errors_keep_their_priority()
    {
        var missing = Guid.NewGuid();
        await AssertErrorAsync(await _client.PostAsync($"/api/v1/forms/{missing}/responses", R7Json("{")), HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
        var owner = await CreateMemberAsync("R8 Transport");
        foreach (var content in new HttpContent?[] { null, new StringContent("{}"), R7Json("{"), R7Json("{\"submittedAt\":null}"), R7Json("[]") })
            await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{missing}/responses", owner.Cookie, content),
                HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms/bad/responses", owner.Cookie, R7Json("{}")),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{missing}/responses", owner.Cookie, R7Json("{}")),
            HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        var form = await CreateR8FormAsync(owner.Cookie);
        await AssertErrorAsync(await R8SubmitAsync(form, "access_token=bad", "{"), HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");
        await ExecuteAsync("UPDATE users SET status='disabled' WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        await AssertErrorAsync(await R8SubmitAsync(form, owner.Cookie, "{"), HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");
    }

    [TestMethod]
    public async Task R8_deadline_then_single_then_answers_priority_and_failed_validation_has_no_rows()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R8 Gate Priority");
        var form = await CreateR8FormAsync(owner.Cookie, deadline: clock.Now.AddSeconds(5));
        var invalid = await R8SubmitAsync(form, owner.Cookie, "{}");
        await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertR7DetailAsync(invalid, "answers", "REQUIRED_FIELD");
        Assert.AreEqual(0L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}")).StatusCode);
        clock.Reset();
        await AssertErrorAsync(await R8SubmitAsync(form, owner.Cookie, "{}"), HttpStatusCode.Conflict, "RESPONSE_ALREADY_SUBMITTED");
        Assert.AreEqual(1, clock.Calls);
        clock.Now = clock.Now.AddSeconds(5);
        await AssertErrorAsync(await R8SubmitAsync(form, owner.Cookie, "{}"), HttpStatusCode.Conflict, "SUBMISSION_DEADLINE_EXCEEDED");
        Assert.AreEqual(1L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(0L, await R8AnswerCountAsync(form.Id));
    }

    [TestMethod]
    [DataRow(-10, true)]
    [DataRow(0, false)]
    [DataRow(10, false)]
    public async Task R8_deadline_boundary_uses_one_sample_and_does_not_close_form(int deltaTicks, bool accepted)
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R8 Deadline");
        var deadline = clock.Now.AddSeconds(5);
        var form = await CreateR8FormAsync(owner.Cookie, deadline: deadline);
        var before = await ReadDefinitionRowsAsync(form.Id);
        clock.Now = deadline.AddTicks(deltaTicks);
        clock.Reset();
        var submit = await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}");
        if (accepted)
        {
            Assert.AreEqual(HttpStatusCode.Created, submit.StatusCode, await submit.Content.ReadAsStringAsync());
            using var receipt = await ReadJsonAsync(submit);
            Assert.AreEqual(clock.Now, receipt.RootElement.GetProperty("submittedAt").GetDateTimeOffset());
            await AssertR8PersistedReceiptAsync(receipt.RootElement);
        }
        else await AssertErrorAsync(submit, HttpStatusCode.Conflict, "SUBMISSION_DEADLINE_EXCEEDED");
        Assert.AreEqual(1, clock.Calls);
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(form.Id));
    }

    [TestMethod]
    public async Task R8_invalid_answer_paths_and_required_omission_do_not_write_rows()
    {
        var owner = await CreateMemberAsync("R8 Invalid Answers");
        var form = await CreateR8FormAsync(owner.Cookie, requiredText: true);
        var cases = new (string Body, string Path, string Code)[]
        {
            ("{\"answers\":[]}", $"questions.{form.Questions[0]}", "REQUIRED_FIELD"),
            ($$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":" "}]}""", "answers[0].value", "REQUIRED_FIELD"),
            ($$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":null}]}""", "answers[0].value", "INVALID_VALUE"),
            ($$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":"\u0000"}]}""", "answers[0].value", "INVALID_VALUE"),
            ($$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":"\uD800"}]}""", "answers[0].value", "INVALID_VALUE"),
            ($$"""{"answers":[{"questionId":"{{Guid.NewGuid()}}","value":null}]}""", "answers[0].questionId", "QUESTION_NOT_FOUND"),
            ($$"""{"answers":[{"questionId":"{{form.Questions[0]}}","value":"ok"},{"questionId":"{{form.Questions[2]}}","value":"{{form.Options[1]}}"}]}""", "answers[1].value", "OPTION_NOT_FOUND")
        };
        foreach (var (body, path, code) in cases)
        {
            var submit = await R8SubmitAsync(form, owner.Cookie, body);
            await AssertErrorAsync(submit, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            await AssertR7DetailAsync(submit, path, code);
        }
        Assert.AreEqual(0L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(0L, await R8AnswerCountAsync(form.Id));
    }

    [TestMethod]
    public async Task R8_receipt_breaks_equal_question_sort_order_by_question_id()
    {
        var owner = await CreateMemberAsync("R8 Receipt Order");
        var form = await CreateR8FormAsync(owner.Cookie);
        // Test-only SQL fixture exercises the documented tie-break; production definition validation stays intact.
        await ExecuteAsync("UPDATE questions SET sort_order=0 WHERE form_id=@id", new NpgsqlParameter("id", form.Id));
        var submit = await R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form));
        Assert.AreEqual(HttpStatusCode.Created, submit.StatusCode, await submit.Content.ReadAsStringAsync());
        using var receipt = await ReadJsonAsync(submit);
        CollectionAssert.AreEqual(form.Questions.Order().ToArray(), receipt.RootElement.GetProperty("answers").EnumerateArray()
            .Select(answer => answer.GetProperty("questionId").GetGuid()).ToArray());
        await AssertR8PersistedReceiptAsync(receipt.RootElement);
    }

    private async Task UseR8FactoryAsync(R8Clock? clock = null, IReadOnlyList<IInterceptor>? interceptors = null, int? maxBatchSize = null)
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new CustomFormServiceApiFactory(timeProvider: clock, interceptors: interceptors, maxBatchSize: maxBatchSize);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        if (maxBatchSize is not null)
        {
            using var scope = _factory.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;
        }
    }

    private async Task<R8Form> CreateR8FormAsync(string cookie, string mode = "single", DateTimeOffset? deadline = null, bool requiredText = false)
    {
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", cookie, JsonContent.Create(new
        {
            title = "R8 submission", responseMode = mode, responseIdentity = "anonymous",
            submissionDeadlineAt = deadline?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            questions = new object[]
            {
                new { type = "text", title = "Text", sortOrder = 0, required = requiredText },
                new { type = "number", title = "Number", sortOrder = 1 },
                new { type = "single_choice", title = "Single", sortOrder = 2,
                    options = new[] { new { label = "One", value = "one", sortOrder = 0 } } },
                new { type = "multiple_choice", title = "Multiple", sortOrder = 3,
                    options = new[] { new { label = "One", value = "one", sortOrder = 0 }, new { label = "Two", value = "two", sortOrder = 1 } } },
                new { type = "rating", title = "Rating", sortOrder = 4, config = new { min = 0, max = 10 } }
            }
        }));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(create);
        var root = body.RootElement;
        var id = root.GetProperty("id").GetGuid();
        var questions = root.GetProperty("questions").EnumerateArray().ToArray();
        var fixture = new R8Form(id, questions.Select(question => question.GetProperty("id").GetGuid()).ToArray(),
            questions.SelectMany(question => question.GetProperty("options").EnumerateArray()).Select(option => option.GetProperty("id").GetGuid()).ToArray());
        var publish = await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{id}/publish", cookie);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode, await publish.Content.ReadAsStringAsync());
        return fixture;
    }

    private static string R8AllAnswers(R8Form form) => $$"""
        {"answers":[
          {"questionId":"{{form.Questions[4]}}","value":5},
          {"questionId":"{{form.Questions[3]}}","value":["{{form.Options[2].ToString("B").ToUpperInvariant()}}","{{form.Options[1]}}"]},
          {"questionId":"{{form.Questions[2]}}","value":"{{form.Options[0].ToString("N").ToUpperInvariant()}}"},
          {"questionId":"{{form.Questions[1]}}","value":1e0},
          {"questionId":"{{form.Questions[0]}}","value":"  Hello 😀  "}
        ]}
        """;

    private Task<HttpResponseMessage> R8SubmitAsync(R8Form form, string cookie, string body, CancellationToken cancellationToken = default) =>
        R8RequestAsync(HttpMethod.Post, $"/api/v1/forms/{form.Id}/responses", cookie, R7Json(body), cancellationToken);

    private async Task<HttpResponseMessage> R8RequestAsync(HttpMethod method, string path, string cookie, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request, cancellationToken);
    }

    private Task<long> R8ResponseCountAsync(Guid formId) => ScalarAsync<long>("SELECT COUNT(*) FROM responses WHERE form_id=@id", new NpgsqlParameter("id", formId));
    private Task<long> R8AnswerCountAsync(Guid formId) => ScalarAsync<long>("SELECT COUNT(*) FROM answers WHERE response_id IN (SELECT id FROM responses WHERE form_id=@id)", new NpgsqlParameter("id", formId));

    private async Task AssertR8PersistedReceiptAsync(JsonElement receipt)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = receipt.GetProperty("id").GetGuid();
        var persisted = await database.Responses.AsNoTracking().SingleAsync(response => response.Id == id);
        Assert.AreEqual(persisted.SubmittedAt, receipt.GetProperty("submittedAt").GetDateTimeOffset());
        var persistedAnswers = await database.Answers.AsNoTracking().Where(answer => answer.ResponseId == id)
            .OrderBy(answer => answer.Question.SortOrder).ThenBy(answer => answer.QuestionId).ToArrayAsync();
        var receiptAnswers = receipt.GetProperty("answers").EnumerateArray().ToArray();
        Assert.HasCount(persistedAnswers.Length, receiptAnswers);
        for (var index = 0; index < persistedAnswers.Length; index++)
        {
            Assert.AreEqual(persistedAnswers[index].QuestionId, receiptAnswers[index].GetProperty("questionId").GetGuid());
            var persistedValue = persistedAnswers[index].Value;
            var receiptValue = receiptAnswers[index].GetProperty("value");
            Assert.IsTrue(JsonElement.DeepEquals(persistedValue, receiptValue), "Receipt must contain the actual saved value.");
            if (persistedValue.ValueKind == JsonValueKind.Number)
                Assert.AreEqual(persistedValue.GetRawText(), receiptValue.GetRawText(), "Saved numbers must not be rounded during Receipt serialization.");
        }
    }

    private sealed record R8Form(Guid Id, Guid[] Questions, Guid[] Options);

    private sealed class R8Clock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks / 10 * 10;
        private int _calls;
        public DateTimeOffset Now { get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero); set => Interlocked.Exchange(ref _ticks, value.UtcTicks); }
        public int Calls => Volatile.Read(ref _calls);
        public void Reset() => Interlocked.Exchange(ref _calls, 0);
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref _calls); return Now; }
    }
}
