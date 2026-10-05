using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow("single", true)]
    [DataRow("single", false)]
    [DataRow("multiple", true)]
    [DataRow("multiple", false)]
    public async Task R9_latest_returns_persisted_receipt_and_GET_has_no_writes(string mode, bool ownerSubmits)
    {
        var capture = new SqlCaptureInterceptor();
        await UseR8FactoryAsync(interceptors: [capture]);
        var owner = await CreateMemberAsync("R9 Owner");
        var respondent = ownerSubmits ? owner : await CreateMemberAsync("R9 Respondent");
        var form = await CreateR8FormAsync(owner.Cookie, mode: mode);
        using var submitted = await ReadJsonAsync(await R8SubmitAsync(form, respondent.Cookie, R8AllAnswers(form)));
        var before = await R9ReadRowsAsync(form.Id);
        capture.Clear();
        using var latest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, respondent.Cookie));
        Assert.IsTrue(JsonElement.DeepEquals(submitted.RootElement, latest.RootElement));
        await AssertR8PersistedReceiptAsync(latest.RootElement);
        CollectionAssert.AreEquivalent(new[] { "id", "formId", "submittedAt", "answers" },
            latest.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        foreach (var answer in latest.RootElement.GetProperty("answers").EnumerateArray())
            CollectionAssert.AreEquivalent(new[] { "questionId", "value" }, answer.EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEqual(form.Questions, latest.RootElement.GetProperty("answers").EnumerateArray()
            .Select(answer => answer.GetProperty("questionId").GetGuid()).ToArray());
        Assert.AreEqual(1, await R9GetCountAsync(form.Id, owner.Cookie));
        Assert.AreEqual(before, await R9ReadRowsAsync(form.Id));
        Assert.IsFalse(capture.Commands.Any(sql => sql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) || sql.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(capture.Commands.Any(sql => sql.Contains("ORDER BY r.submitted_at DESC, r.id DESC", StringComparison.Ordinal)
            && sql.Contains("LIMIT", StringComparison.Ordinal)), "Latest must be selected and limited in PostgreSQL.");
        Assert.IsFalse(capture.Commands.Any(sql => sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task R9_latest_uses_whole_response_without_backfilling_optional_answers(bool answerOneQuestion)
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R9 Whole Response");
        var form = await CreateR8FormAsync(owner.Cookie, mode: "multiple");
        using var first = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form)));
        clock.Now = clock.Now.AddSeconds(1);
        var body = answerOneQuestion
            ? $$"""{"answers":[{"questionId":"{{form.Questions[1]}}","value":0.123456789012345678901234567890123456789}]}"""
            : "{\"answers\":[]}";
        using var second = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, body));
        using var latest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, owner.Cookie));
        Assert.IsTrue(JsonElement.DeepEquals(second.RootElement, latest.RootElement));
        Assert.AreNotEqual(first.RootElement.GetProperty("id").GetGuid(), latest.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(answerOneQuestion ? 1 : 0, latest.RootElement.GetProperty("answers").GetArrayLength());
        await AssertR8PersistedReceiptAsync(latest.RootElement);
        Assert.AreEqual(2L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(answerOneQuestion ? 6L : 5L, await R8AnswerCountAsync(form.Id));
        Assert.AreEqual(1, await R9GetCountAsync(form.Id, owner.Cookie));
    }

    [TestMethod]
    public async Task R9_latest_timestamp_precedes_UUID_and_same_timestamp_uses_UUID_descending()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R9 Tie Break");
        var form = await CreateR8FormAsync(owner.Cookie, mode: "multiple");
        using var first = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}"));
        using var second = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}"));
        // Only these zero-Answer test Responses get known IDs: older high ID versus later low ID.
        var high = Guid.Parse("ffffffff-0000-0000-0000-000000000009");
        var low = Guid.Parse("00000001-0000-0000-0000-000000000009");
        await ExecuteAsync("UPDATE responses SET id=@newId WHERE id=@oldId",
            new NpgsqlParameter("newId", high), new NpgsqlParameter("oldId", first.RootElement.GetProperty("id").GetGuid()));
        await ExecuteAsync("UPDATE responses SET id=@newId WHERE id=@oldId",
            new NpgsqlParameter("newId", low), new NpgsqlParameter("oldId", second.RootElement.GetProperty("id").GetGuid()));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var latest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, owner.Cookie));
            Assert.AreEqual(high, latest.RootElement.GetProperty("id").GetGuid());
        }
        await ExecuteAsync("UPDATE responses SET submitted_at=submitted_at + interval '1 microsecond' WHERE id=@id",
            new NpgsqlParameter("id", low));
        using var newer = await ReadJsonAsync(await R9GetLatestAsync(form.Id, owner.Cookie));
        Assert.AreEqual(low, newer.RootElement.GetProperty("id").GetGuid());
    }

    [TestMethod]
    public async Task R9_count_deduplicates_successful_users_including_owner_empty_answers_and_disabled_history()
    {
        var owner = await CreateMemberAsync("R9 Count Owner");
        var first = await CreateMemberAsync("R9 First Respondent");
        var second = await CreateMemberAsync("R9 Second Respondent");
        var form = await CreateR8FormAsync(owner.Cookie, mode: "multiple");
        Assert.AreEqual(0, await R9GetCountAsync(form.Id, owner.Cookie));
        foreach (var member in new[] { owner, first, first, second })
            Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, member.Cookie, "{\"answers\":[]}")).StatusCode);
        await ExecuteAsync("UPDATE users SET status='disabled'::user_status WHERE id=@id", new NpgsqlParameter("id", second.Id));
        Assert.AreEqual(3, await R9GetCountAsync(form.Id, owner.Cookie));
        Assert.AreEqual(4L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(0L, await R8AnswerCountAsync(form.Id));
        using var list = await ReadJsonAsync(await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms", owner.Cookie));
        Assert.AreEqual(3, list.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == form.Id).GetProperty("count").GetInt32());
    }

    [TestMethod]
    [DataRow("draft", false)]
    [DataRow("published", false)]
    [DataRow("published", true)]
    [DataRow("closed", false)]
    [DataRow("closed", true)]
    public async Task R9_state_owner_and_empty_data_matrix(string status, bool hasResponse)
    {
        var owner = await CreateMemberAsync("R9 Matrix Owner");
        var other = await CreateMemberAsync("R9 Matrix Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        if (hasResponse)
            Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}")).StatusCode);
        if (status == "closed")
            Assert.AreEqual(HttpStatusCode.OK, (await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{form.Id}/close", owner.Cookie)).StatusCode);
        else if (status == "draft")
            // Empty test fixture only; no product transition back to Draft is introduced.
            await ExecuteAsync("UPDATE forms SET status='draft'::form_status WHERE id=@id", new NpgsqlParameter("id", form.Id));
        if (hasResponse)
        {
            using var latest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, owner.Cookie));
            Assert.AreEqual(0, latest.RootElement.GetProperty("answers").GetArrayLength());
        }
        else await AssertErrorAsync(await R9QueryAsync(form.Id, owner.Cookie, false), HttpStatusCode.NotFound, "RESPONSE_NOT_FOUND");
        await AssertErrorAsync(await R9QueryAsync(form.Id, other.Cookie, false), HttpStatusCode.NotFound,
            status == "draft" ? "FORM_NOT_FOUND" : "RESPONSE_NOT_FOUND");
        Assert.AreEqual(hasResponse ? 1 : 0, await R9GetCountAsync(form.Id, owner.Cookie));
        await AssertErrorAsync(await R9QueryAsync(form.Id, other.Cookie, true), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
    }

    [TestMethod]
    [DataRow("anonymous", "owner_only")]
    [DataRow("anonymous", "respondents")]
    [DataRow("anonymous", "public")]
    [DataRow("identified", "owner_only")]
    [DataRow("identified", "respondents")]
    [DataRow("identified", "public")]
    public async Task R9_deadline_and_result_policies_do_not_change_latest_or_count_access(string identity, string visibility)
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R9 Policy Owner");
        var other = await CreateMemberAsync("R9 Policy Respondent");
        var form = await CreateR8FormAsync(owner.Cookie, deadline: clock.Now.AddSeconds(10));
        // Internal fixture setup varies frozen Identity; the API does not expose this mutation.
        await ExecuteAsync("UPDATE forms SET response_identity=CAST(@identity AS response_identity) WHERE id=@id",
            new NpgsqlParameter("identity", identity), new NpgsqlParameter("id", form.Id));
        Assert.AreEqual(HttpStatusCode.OK, (await SendWithCookieAsync(HttpMethod.Patch,
            $"/api/v1/forms/{form.Id}/result-visibility", owner.Cookie, R7Json($$"""{"resultVisibility":"{{visibility}}"}"""))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, other.Cookie, R8AllAnswers(form))).StatusCode);
        clock.Now = clock.Now.AddSeconds(20);
        foreach (var close in new[] { false, true })
        {
            if (close) Assert.AreEqual(HttpStatusCode.OK, (await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{form.Id}/close", owner.Cookie)).StatusCode);
            using var latest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, other.Cookie));
            await AssertR8PersistedReceiptAsync(latest.RootElement);
            await AssertErrorAsync(await R9QueryAsync(form.Id, owner.Cookie, false), HttpStatusCode.NotFound, "RESPONSE_NOT_FOUND");
            Assert.AreEqual(1, await R9GetCountAsync(form.Id, owner.Cookie));
            await AssertErrorAsync(await R9QueryAsync(form.Id, other.Cookie, true), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        }
    }

    [TestMethod]
    public async Task R9_latest_is_scoped_to_current_user_and_form()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R9 Scope Owner");
        var other = await CreateMemberAsync("R9 Scope Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        var otherForm = await CreateR8FormAsync(owner.Cookie);
        using var own = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}"));
        clock.Now = clock.Now.AddSeconds(1);
        using var theirs = await ReadJsonAsync(await R8SubmitAsync(form, other.Cookie, R8AllAnswers(form)));
        clock.Now = clock.Now.AddSeconds(1);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(otherForm, owner.Cookie, R8AllAnswers(otherForm))).StatusCode);
        using var latest = await ReadJsonAsync(await SendWithCookieAsync(HttpMethod.Get,
            $"{R9Path(form.Id, false)}?userId={other.Id}", owner.Cookie));
        Assert.IsTrue(JsonElement.DeepEquals(own.RootElement, latest.RootElement));
        using var otherLatest = await ReadJsonAsync(await R9GetLatestAsync(form.Id, other.Cookie));
        Assert.IsTrue(JsonElement.DeepEquals(theirs.RootElement, otherLatest.RootElement));
        await AssertErrorAsync(await R9QueryAsync(otherForm.Id, other.Cookie, false), HttpStatusCode.NotFound, "RESPONSE_NOT_FOUND");
        Assert.AreEqual(2, await R9GetCountAsync(form.Id, owner.Cookie));
        Assert.AreEqual(1, await R9GetCountAsync(otherForm.Id, owner.Cookie));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task R9_authentication_UUID_and_resource_errors_keep_priority(bool count)
    {
        var owner = await CreateMemberAsync("R9 Errors Owner");
        var other = await CreateMemberAsync("R9 Errors Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}")).StatusCode);
        foreach (var id in new[] { "bad-uuid", form.Id.ToString(), Guid.NewGuid().ToString() })
        {
            var path = R9Path(id, count);
            await AssertErrorAsync(await _client.GetAsync(path), HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
            await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, path, "access_token=invalid"), HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");
            await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, path,
                R9TokenCookie(owner.Id, DateTime.UtcNow.AddHours(-1))), HttpStatusCode.Unauthorized, "AUTH_TOKEN_EXPIRED");
            await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, path,
                R9TokenCookie(Guid.NewGuid(), DateTime.UtcNow.AddHours(1))), HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");
        }
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, R9Path("bad-uuid", count), owner.Cookie), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertErrorAsync(await R9QueryAsync(Guid.NewGuid(), owner.Cookie, count), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        await ExecuteAsync("UPDATE users SET status='disabled'::user_status WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, R9Path("bad-uuid", count), owner.Cookie), HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");
        await AssertErrorAsync(await R9QueryAsync(form.Id, owner.Cookie, count), HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");
        await ExecuteAsync("UPDATE users SET status='active'::user_status WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        Assert.AreEqual(HttpStatusCode.NoContent, (await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{form.Id}", owner.Cookie)).StatusCode);
        var before = await R9ReadRowsAsync(form.Id);
        foreach (var member in new[] { owner, other })
            await AssertErrorAsync(await R9QueryAsync(form.Id, member.Cookie, count), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        await AssertErrorAsync(await _client.GetAsync(R9Path(form.Id, count)), HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
        Assert.AreEqual(before, await R9ReadRowsAsync(form.Id));
        Assert.AreEqual(1L, await R8ResponseCountAsync(form.Id));
    }

    [TestMethod]
    [DataRow(false, "delete")]
    [DataRow(true, "delete")]
    [DataRow(false, "submit")]
    [DataRow(true, "submit")]
    public async Task R9_completed_Form_SELECT_establishes_snapshot_before_concurrent_write(bool count, string write)
    {
        var clock = new R8Clock();
        var gate = new R9SnapshotGate();
        await UseR8FactoryAsync(clock, [gate]);
        var owner = await CreateMemberAsync("R9 Snapshot Owner");
        var other = await CreateMemberAsync("R9 Snapshot Other");
        var form = await CreateR8FormAsync(owner.Cookie, mode: "multiple");
        using var original = await ReadJsonAsync(await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}"));
        gate.FormId = form.Id;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var read = R8RequestAsync(HttpMethod.Get, R9Path(form.Id, count), owner.Cookie, null, cancellation.Token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(gate.FormSelectExecuted, "Gate opens only after the actual Form SELECT completes, not at BeginTransaction.");
            clock.Now = clock.Now.AddSeconds(1);
            var writer = write == "delete"
                ? await R8RequestAsync(HttpMethod.Delete, $"/api/v1/forms/{form.Id}", owner.Cookie, null, cancellation.Token)
                : await R8SubmitAsync(form, count ? other.Cookie : owner.Cookie, R8AllAnswers(form), cancellation.Token);
            Assert.AreEqual(write == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.Created,
                writer.StatusCode, await writer.Content.ReadAsStringAsync());
            Assert.IsFalse(read.IsCompleted, "Writer commits while the snapshot GET is paused.");
            gate.Release.TrySetResult();
            var readResult = await read.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(HttpStatusCode.OK, readResult.StatusCode, await readResult.Content.ReadAsStringAsync());
            using var snapshot = await ReadJsonAsync(readResult);
            if (count) Assert.AreEqual(1, snapshot.RootElement.GetProperty("count").GetInt32());
            else Assert.IsTrue(JsonElement.DeepEquals(original.RootElement, snapshot.RootElement), "New committed Response cannot enter an established snapshot.");
            if (write == "delete")
                await AssertErrorAsync(await R9QueryAsync(form.Id, owner.Cookie, count), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
            else if (count) Assert.AreEqual(2, await R9GetCountAsync(form.Id, owner.Cookie));
            else
            {
                using var fresh = await ReadJsonAsync(await R9GetLatestAsync(form.Id, owner.Cookie));
                Assert.AreNotEqual(original.RootElement.GetProperty("id").GetGuid(), fresh.RootElement.GetProperty("id").GetGuid());
                Assert.AreEqual(5, fresh.RootElement.GetProperty("answers").GetArrayLength());
            }
            Console.WriteLine($"R9 snapshot evidence: endpoint={(count ? "count" : "latest")}, Form SELECT executed in RepeatableRead, {write} committed before release; snapshot and next GET verified.");
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
            cancellation.Cancel();
            await DrainR8RequestsAsync(read);
        }
    }

    private static string R9Path(Guid formId, bool count) => R9Path(formId.ToString(), count);
    private static string R9Path(string formId, bool count) => $"/api/v1/forms/{formId}/" + (count ? "respondent-count" : "responses/me/latest");
    private Task<HttpResponseMessage> R9QueryAsync(Guid formId, string cookie, bool count) => SendWithCookieAsync(HttpMethod.Get, R9Path(formId, count), cookie);

    private async Task<HttpResponseMessage> R9GetLatestAsync(Guid formId, string cookie)
    {
        var result = await R9QueryAsync(formId, cookie, false);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode, await result.Content.ReadAsStringAsync());
        return result;
    }

    private async Task<int> R9GetCountAsync(Guid formId, string cookie)
    {
        using var result = await R9QueryAsync(formId, cookie, true);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode, await result.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(result);
        CollectionAssert.AreEqual(new[] { "count" }, body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        return body.RootElement.GetProperty("count").GetInt32();
    }

    private Task<string> R9ReadRowsAsync(Guid formId) => ScalarAsync<string>("""
        SELECT jsonb_build_object(
            'form', (SELECT to_jsonb(f) FROM forms f WHERE id=@id),
            'questions', (SELECT jsonb_agg(to_jsonb(q) ORDER BY id) FROM questions q WHERE form_id=@id),
            'options', (SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM options o WHERE question_id IN (SELECT id FROM questions WHERE form_id=@id)),
            'responses', (SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM responses r WHERE form_id=@id),
            'answers', (SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM answers a WHERE response_id IN (SELECT id FROM responses WHERE form_id=@id))
        )::text
        """, new NpgsqlParameter("id", formId));

    private static string R9TokenCookie(Guid userId, DateTime expires)
    {
        var token = new JwtSecurityToken(CustomFormServiceApiFactory.JwtIssuer, CustomFormServiceApiFactory.JwtAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], DateTime.UtcNow.AddHours(-2), expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomFormServiceApiFactory.JwtSigningKey)), SecurityAlgorithms.HmacSha256));
        return $"access_token={new JwtSecurityTokenHandler().WriteToken(token)}";
    }

    private sealed class R9SnapshotGate : DbCommandInterceptor
    {
        private int _entered;
        public Guid? FormId { get; set; }
        public bool FormSelectExecuted { get; private set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (FormId is { } id && command.Transaction?.IsolationLevel == IsolationLevel.RepeatableRead
                && command.CommandText.Contains("FROM forms AS", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid value && value == id)
                && Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                FormSelectExecuted = true;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
