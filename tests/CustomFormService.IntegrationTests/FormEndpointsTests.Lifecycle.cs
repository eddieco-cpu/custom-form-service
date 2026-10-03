using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    public async Task R7_publish_without_body_normalizes_only_candidate_and_preserves_entire_definition()
    {
        var capture = new SqlCaptureInterceptor();
        await UseCandidateInterceptorsAsync(capture);
        var owner = await CreateMemberAsync("R7 Freeze Owner");
        var formId = await CreateR7FormAsync(owner.Cookie);
        // Deliberately noncanonical but valid DB values make normalization observable.
        // These changes affect this test's synthetic aggregate only.
        await ExecuteAsync("""
            UPDATE forms SET title = '  raw form  ', description = '  raw description  ' WHERE id = @id;
            UPDATE questions SET title = '  raw question  ', description = '  raw description  ',
                config = CASE WHEN type = 'text' THEN '{"placeholder":"  raw placeholder  "}'::jsonb ELSE config END
                WHERE form_id = @id;
            UPDATE options SET label = '  raw label  ', value = '  raw value  '
                WHERE question_id IN (SELECT id FROM questions WHERE form_id = @id);
            """, new NpgsqlParameter("id", formId));
        var rows = await ReadDefinitionRowsAsync(formId);
        capture.Clear();
        var response = await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{formId}/publish", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var published = await ReadJsonAsync(response);
        Assert.AreEqual("published", published.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("  raw form  ", published.RootElement.GetProperty("title").GetString());
        AssertOnlyFormColumnsChanged(rows, await ReadDefinitionRowsAsync(formId), "status", "updated_at");
        AssertOnlyR7SqlColumns(capture.Commands, "status", "updated_at");
        using var actual = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.AreEqual(actual.RootElement.GetRawText(), published.RootElement.GetRawText());
    }

    [TestMethod]
    public async Task R7_close_without_body_does_not_revalidate_frozen_definition_or_auto_close_at_deadline()
    {
        var owner = await CreateMemberAsync("R7 Close Owner");
        var other = await CreateMemberAsync("R7 Reader");
        var formId = await CreateR7FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, (await R7ActionAsync("publish", formId, owner.Cookie)).StatusCode);
        await ExecuteAsync("""
            UPDATE forms SET submission_deadline_at = '2000-01-01T00:00:00Z' WHERE id = @id;
            UPDATE questions SET config = '{}' WHERE form_id = @id AND type = 'rating';
            """, new NpgsqlParameter("id", formId));
        using var stillPublished = await GetFormJsonAsync(formId, other.Cookie);
        Assert.AreEqual("published", stillPublished.RootElement.GetProperty("status").GetString());
        var before = await ReadDefinitionRowsAsync(formId);
        var close = await R7ActionAsync("close", formId, owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode, await close.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(close);
        Assert.AreEqual("closed", body.RootElement.GetProperty("status").GetString());
        AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(formId), "status", "updated_at");
        await AssertErrorAsync(await R7ActionAsync("close", formId, owner.Cookie), HttpStatusCode.Conflict, "FORM_ALREADY_CLOSED");
        await AssertErrorAsync(await R7ActionAsync("publish", formId, owner.Cookie), HttpStatusCode.Conflict, "FORM_INVALID_STATE_TRANSITION");
        foreach (var patch in new[] { "{}", "{\"title\":\"changed\"}" })
            await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
                R7Json(patch)), HttpStatusCode.Conflict, "FORM_NOT_EDITABLE");
    }

    [TestMethod]
    [DataRow("empty", "questions", "REQUIRED_FIELD")]
    [DataRow("single_choice", "questions[0].options", "INSUFFICIENT_OPTIONS")]
    [DataRow("multiple_choice", "questions[0].options", "INSUFFICIENT_OPTIONS")]
    [DataRow("rating", "questions[0].config.min", "INVALID_QUESTION_CONFIG")]
    public async Task R7_incomplete_draft_can_be_saved_but_publish_fails_atomically(string type, string path, string code)
    {
        var owner = await CreateMemberAsync("R7 Incomplete Owner");
        var id = type == "empty" ? await CreateSimpleFormAsync(owner.Cookie, "Empty draft")
            : await CreateConfiguredQuestionAsync(owner.Cookie, type, ParseJson("{}"));
        var before = await ReadDefinitionRowsAsync(id);
        var response = await R7ActionAsync("publish", id, owner.Cookie);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "FORM_NOT_PUBLISHABLE");
        await AssertR7DetailAsync(response, path, code);
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    public async Task R7_publish_reuses_structural_rules_and_collects_precise_details_without_writes()
    {
        var owner = await CreateMemberAsync("R7 Invalid Candidate Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        await ExecuteAsync("""
            UPDATE questions SET config = '{"min":5,"max":2}' WHERE form_id = @id AND type = 'rating';
            UPDATE questions SET sort_order = 0 WHERE form_id = @id AND type = 'number';
            UPDATE options SET label = '   ' WHERE question_id IN (SELECT id FROM questions WHERE form_id = @id);
            UPDATE forms SET submission_deadline_at = '2000-01-01T00:00:00Z' WHERE id = @id;
            """, new NpgsqlParameter("id", id));
        var before = await ReadDefinitionRowsAsync(id);
        var response = await R7ActionAsync("publish", id, owner.Cookie);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "FORM_NOT_PUBLISHABLE");
        using var error = await ReadJsonAsync(response);
        var codes = error.RootElement.GetProperty("error").GetProperty("details").EnumerateArray()
            .Select(detail => detail.GetProperty("code").GetString()).ToArray();
        foreach (var code in new[] { "INVALID_QUESTION_CONFIG", "DUPLICATE_SORT_ORDER", "REQUIRED_FIELD", "INVALID_VALUE" })
            CollectionAssert.Contains(codes, code);
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public async Task R7_publish_deadline_uses_locked_server_time_and_actual_storage_value(int microseconds)
    {
        var clock = new R7Clock();
        await UseR7ClockAsync(clock);
        var owner = await CreateMemberAsync("R7 Deadline Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        var now = DateTimeOffset.UtcNow.AddHours(1);
        clock.Now = new DateTimeOffset(now.Ticks / 10 * 10, TimeSpan.Zero);
        await ExecuteAsync("UPDATE forms SET submission_deadline_at = @deadline WHERE id = @id;",
            new NpgsqlParameter("deadline", clock.Now.AddTicks(microseconds * 10)), new NpgsqlParameter("id", id));
        var before = await ReadDefinitionRowsAsync(id);
        var response = await R7ActionAsync("publish", id, owner.Cookie);
        if (microseconds > 0)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(id), "status", "updated_at");
        }
        else
        {
            await AssertErrorAsync(response, HttpStatusCode.BadRequest, "FORM_NOT_PUBLISHABLE");
            await AssertR7DetailAsync(response, "submissionDeadlineAt", "INVALID_VALUE");
            Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
        }
    }

    [TestMethod]
    [DataRow("publish")]
    [DataRow("close")]
    [DataRow("result-visibility")]
    public async Task R7_errors_follow_auth_parse_existence_owner_and_domain_order(string action)
    {
        var owner = await CreateMemberAsync("R7 Priority Owner");
        var other = await CreateMemberAsync("R7 Priority Other");
        var id = await CreateSimpleFormAsync(owner.Cookie, "Incomplete priority draft");
        var content = action == "result-visibility" ? "{}" : null;
        async Task<HttpResponseMessage> Send(string target, string cookie)
        {
            using var request = new HttpRequestMessage(action == "result-visibility" ? HttpMethod.Patch : HttpMethod.Post,
                $"/api/v1/forms/{target}/{action}") { Content = content is null ? null : R7Json(content) };
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            return await _client.SendAsync(request);
        }
        await AssertErrorAsync(await Send("bad-id", ""), HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
        await AssertErrorAsync(await Send("bad-id", "access_token=bad-token"), HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");
        await AssertErrorAsync(await Send("bad-id", owner.Cookie), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertErrorAsync(await Send(Guid.NewGuid().ToString(), other.Cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        var before = await ReadDefinitionRowsAsync(id);
        await AssertErrorAsync(await Send(id.ToString(), other.Cookie), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
        var expectedCode = action == "publish" ? "FORM_NOT_PUBLISHABLE"
            : action == "close" ? "FORM_INVALID_STATE_TRANSITION" : "VALIDATION_ERROR";
        await AssertErrorAsync(await Send(id.ToString(), owner.Cookie),
            action == "close" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, expectedCode);
        await ExecuteAsync("UPDATE forms SET status = 'published' WHERE id = @id;", new NpgsqlParameter("id", id));
        await AssertErrorAsync(await Send(id.ToString(), other.Cookie), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        if (action == "publish")
            await AssertErrorAsync(await Send(id.ToString(), owner.Cookie), HttpStatusCode.Conflict, "FORM_ALREADY_PUBLISHED");
        await ExecuteAsync("UPDATE forms SET deleted_at = now() WHERE id = @id;", new NpgsqlParameter("id", id));
        await AssertErrorAsync(await Send(id.ToString(), other.Cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        await ExecuteAsync("UPDATE users SET status = 'disabled' WHERE id = @id;", new NpgsqlParameter("id", owner.Id));
        await AssertErrorAsync(await Send("bad-id", owner.Cookie), HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");
    }

    [TestMethod]
    [DataRow("draft")]
    [DataRow("published")]
    [DataRow("closed")]
    public async Task R7_visibility_is_independent_atomic_and_no_op_in_every_state(string status)
    {
        var capture = new SqlCaptureInterceptor();
        await UseCandidateInterceptorsAsync(capture);
        var owner = await CreateMemberAsync("R7 Visibility Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        await ExecuteAsync("UPDATE forms SET status = @status::form_status WHERE id = @id;",
            new NpgsqlParameter("status", status), new NpgsqlParameter("id", id));
        foreach (var value in new[] { "public", "respondents", "owner_only" })
        {
            var before = await ReadDefinitionRowsAsync(id);
            capture.Clear();
            var response = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility", owner.Cookie,
                JsonContent.Create(new { resultVisibility = value }));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(id), "result_visibility", "updated_at");
            AssertOnlyR7SqlColumns(capture.Commands, "result_visibility", "updated_at");
            using var body = await ReadJsonAsync(response);
            Assert.AreEqual(value, body.RootElement.GetProperty("resultVisibility").GetString());
            Assert.AreEqual(status, body.RootElement.GetProperty("status").GetString());
            using var get = await GetFormJsonAsync(id, owner.Cookie);
            Assert.AreEqual(get.RootElement.GetRawText(), body.RootElement.GetRawText());
            var rows = await ReadDefinitionRowsAsync(id);
            capture.Clear();
            var noop = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility", owner.Cookie,
                JsonContent.Create(new { resultVisibility = value }));
            Assert.AreEqual(HttpStatusCode.OK, noop.StatusCode);
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(id));
            Assert.IsFalse(capture.Commands.Any(command => command.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("{\"resultVisibility\":null}")]
    [DataRow("{\"resultVisibility\":\"Public\"}")]
    [DataRow("{\"resultVisibility\":\"public\",\"status\":\"closed\"}")]
    [DataRow("{\"resultVisibility\":\"public\",\"resultVisibility\":\"respondents\"}")]
    public async Task R7_visibility_rejects_invalid_fields_without_any_write(string json)
    {
        var owner = await CreateMemberAsync("R7 Bad Visibility Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        var rows = await ReadDefinitionRowsAsync(id);
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility",
            owner.Cookie, R7Json(json)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.AreEqual(rows, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    public async Task R7_only_visibility_requires_json_body_and_parsing_precedes_resource_checks()
    {
        var owner = await CreateMemberAsync("R7 Body Owner");
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Patch,
            $"/api/v1/forms/{Guid.NewGuid()}/result-visibility", owner.Cookie, R7Json("{")),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var id = await CreateR7FormAsync(owner.Cookie);
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility",
            owner.Cookie), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        // No input formatter runs for these actions, even if an unused body is not JSON.
        foreach (var action in new[] { "publish", "close" })
            Assert.AreEqual(HttpStatusCode.OK, (await SendWithCookieAsync(HttpMethod.Post,
                $"/api/v1/forms/{id}/{action}", owner.Cookie, new StringContent("unused", Encoding.UTF8, "text/plain"))).StatusCode);
    }

    [TestMethod]
    [DataRow("publish", false)]
    [DataRow("close", false)]
    [DataRow("result-visibility", false)]
    [DataRow("publish", true)]
    [DataRow("close", true)]
    [DataRow("result-visibility", true)]
    public async Task R7_save_and_commit_faults_rollback_all_rows(string action, bool failAtCommit)
    {
        var saveFailure = new R7SaveFailure();
        var commitFailure = new CommitFailureInterceptor();
        await UseCandidateInterceptorsAsync(saveFailure, commitFailure);
        var owner = await CreateMemberAsync("R7 Rollback Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        if (action == "close")
            await ExecuteAsync("UPDATE forms SET status = 'published' WHERE id = @id;", new NpgsqlParameter("id", id));
        var rows = await ReadDefinitionRowsAsync(id);
        saveFailure.Armed = !failAtCommit;
        commitFailure.Armed = failAtCommit;
        var response = await R7ActionAsync(action, id, owner.Cookie);
        saveFailure.Armed = commitFailure.Armed = false;
        await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        Assert.IsTrue(failAtCommit ? commitFailure.Injected : saveFailure.Injected);
        Assert.AreEqual(rows, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow("patch", "publish", "FORM_NOT_PUBLISHABLE")]
    [DataRow("publish", "patch", "FORM_NOT_EDITABLE")]
    [DataRow("publish", "publish", "FORM_ALREADY_PUBLISHED")]
    [DataRow("close", "close", "FORM_ALREADY_CLOSED")]
    [DataRow("publish", "result-visibility", "")]
    [DataRow("result-visibility", "publish", "")]
    [DataRow("close", "result-visibility", "")]
    public async Task R7_writers_share_lock_and_validate_after_the_first_commit(string firstAction, string secondAction, string errorCode)
    {
        var gate = new R7SaveGate();
        await UseCandidateInterceptorsAsync(gate);
        var owner = await CreateMemberAsync("R7 Concurrency Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        using var definition = await GetFormJsonAsync(id, owner.Cookie);
        var ratingId = definition.RootElement.GetProperty("questions")[4].GetProperty("id").GetGuid();
        if (firstAction == "close")
            await ExecuteAsync("UPDATE forms SET status = 'published' WHERE id = @id;", new NpgsqlParameter("id", id));
        var before = await ReadDefinitionRowsAsync(id);
        Task<HttpResponseMessage> Send(string action) => action == "patch"
            ? SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{id}", owner.Cookie,
                JsonContent.Create(new { questions = new[] { new { id = ratingId, config = new { } } } }))
            : R7ActionAsync(action, id, owner.Cookie);
        gate.FormId = id;
        var first = Send(firstAction);
        HttpResponseMessage firstResponse;
        HttpResponseMessage secondResponse;
        try
        {
            var blockerPid = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var second = Send(secondAction);
            await WaitForR7BlockedAsync(blockerPid);
            Assert.IsFalse(second.IsCompleted, "Second writer must be waiting on the first transaction's Form lock.");
            gate.Release.TrySetResult();
            firstResponse = await first;
            secondResponse = await second;
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
        }
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode, await firstResponse.Content.ReadAsStringAsync());
        if (errorCode.Length > 0)
        {
            await AssertErrorAsync(secondResponse, errorCode == "FORM_NOT_PUBLISHABLE" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, errorCode);
            using var winner = await ReadJsonAsync(firstResponse);
            using var actual = await GetFormJsonAsync(id, owner.Cookie);
            Assert.AreEqual(winner.RootElement.GetRawText(), actual.RootElement.GetRawText(), "Rejected writer must not change the winner's result.");
            if (firstAction != "patch")
                AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(id), "status", "updated_at");
        }
        else
        {
            Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode, await secondResponse.Content.ReadAsStringAsync());
            using var actual = await GetFormJsonAsync(id, owner.Cookie);
            Assert.AreEqual(firstAction == "close" ? "closed" : "published", actual.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("public", actual.RootElement.GetProperty("resultVisibility").GetString());
            AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(id), "status", "result_visibility", "updated_at");
        }
    }

    [TestMethod]
    public async Task R7_publish_samples_time_after_waiting_for_the_form_lock()
    {
        var clock = new R7Clock();
        await UseR7ClockAsync(clock);
        var owner = await CreateMemberAsync("R7 Locked Deadline Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        var deadline = clock.Now.AddMinutes(10);
        await ExecuteAsync("UPDATE forms SET submission_deadline_at = @deadline WHERE id = @id;",
            new NpgsqlParameter("deadline", deadline), new NpgsqlParameter("id", id));
        var before = await ReadDefinitionRowsAsync(id);
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT 1 FROM forms WHERE id = @id FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteScalarAsync();
        var publish = R7ActionAsync("publish", id, owner.Cookie);
        try
        {
            await WaitForR7BlockedAsync(connection.ProcessID);
            clock.Now = deadline.AddMinutes(1);
        }
        finally
        {
            await transaction.CommitAsync();
        }
        var response = await publish;
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "FORM_NOT_PUBLISHABLE");
        await AssertR7DetailAsync(response, "submissionDeadlineAt", "INVALID_VALUE");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    private async Task WaitForR7BlockedAsync(int blockerPid)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await ScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE @pid = ANY(pg_blocking_pids(pid)));",
                    new NpgsqlParameter("pid", blockerPid))) return;
            await Task.Delay(25); // Poll the observed wait, not a timing-based ordering assumption.
        }
        Assert.Fail("Expected a PostgreSQL lock waiter blocked by the first writer.");
    }

    private sealed class R7SaveGate : SaveChangesInterceptor
    {
        public Guid? FormId { get; set; }
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var database = eventData.Context!;
            if (FormId is { } id && database.ChangeTracker.Entries<Form>().Any(entry => entry.Entity.Id == id)
                && Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Reached.TrySetResult(((NpgsqlConnection)database.Database.GetDbConnection()).ProcessID);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private async Task<Guid> CreateR7FormAsync(string cookie)
    {
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", cookie, R7Json("""
            {"title":"R7 complete","questions":[
              {"type":"text","title":"Text","sortOrder":0,"config":{"placeholder":"x"}},
              {"type":"number","title":"Number","sortOrder":1,"config":{"min":1e30,"max":1e31}},
              {"type":"single_choice","title":"Single","sortOrder":2,"options":[{"label":"One","value":"one","sortOrder":0}]},
              {"type":"multiple_choice","title":"Multiple","sortOrder":3,"options":[{"label":"Other","value":"other","sortOrder":0}]},
              {"type":"rating","title":"Rating","sortOrder":4,"required":false,"config":{"min":0,"max":100}}
            ]}
            """));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(create);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> R7ActionAsync(string action, Guid id, string cookie) =>
        SendWithCookieAsync(action == "result-visibility" ? HttpMethod.Patch : HttpMethod.Post,
            $"/api/v1/forms/{id}/{action}", cookie,
            action == "result-visibility" ? JsonContent.Create(new { resultVisibility = "public" }) : null);

    private static StringContent R7Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task AssertR7DetailAsync(HttpResponseMessage response, string path, string code)
    {
        using var body = await ReadJsonAsync(response);
        Assert.IsTrue(body.RootElement.GetProperty("error").GetProperty("details").EnumerateArray().Any(detail =>
            detail.GetProperty("path").GetString() == path && detail.GetProperty("code").GetString() == code), body.RootElement.GetRawText());
    }

    private static void AssertOnlyFormColumnsChanged(string before, string after, params string[] columns)
    {
        var oldRows = JsonNode.Parse(before)!;
        var newRows = JsonNode.Parse(after)!;
        foreach (var column in columns)
        {
            Assert.IsFalse(JsonNode.DeepEquals(oldRows["form"]![column], newRows["form"]![column]), $"{column} must change.");
            oldRows["form"]!.AsObject().Remove(column);
            newRows["form"]!.AsObject().Remove(column);
        }
        Assert.IsTrue(JsonNode.DeepEquals(oldRows, newRows), "All other columns, child rows, IDs and timestamps must be identical.");
    }

    private static void AssertOnlyR7SqlColumns(IReadOnlyCollection<string> commands, params string[] columns)
    {
        var updates = System.Text.RegularExpressions.Regex.Matches(string.Join('\n', commands),
            @"UPDATE forms SET (.*?)\s+WHERE", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.HasCount(1, updates);
        var assignments = System.Text.RegularExpressions.Regex.Matches(updates[0].Groups[1].Value, @"(\w+)\s*=")
            .Select(match => match.Groups[1].Value).Order().ToArray();
        CollectionAssert.AreEqual(columns.Order().ToArray(), assignments);
        Assert.IsFalse(commands.Any(command => command.Contains("UPDATE questions", StringComparison.OrdinalIgnoreCase)
            || command.Contains("UPDATE options", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task UseR7ClockAsync(R7Clock clock, params IInterceptor[] interceptors)
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new CustomFormServiceApiFactory(interceptors: interceptors, timeProvider: clock);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    }

    private sealed class R7Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class R7SaveFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                var database = eventData.Context!;
                await using var command = database.Database.GetDbConnection().CreateCommand();
                command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "SELECT 1 / 0;";
                Injected = true;
                await command.ExecuteScalarAsync(cancellationToken);
            }
            return result;
        }
    }
}
