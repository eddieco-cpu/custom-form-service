using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow(2147483647, 100)]
    [DataRow(42949674, 100)]
    [DataRow(1073741825, 4)]
    public async Task Repair_F01_large_page_returns_empty_without_offset_wrap(int page, int size)
    {
        var owner = await CreateMemberAsync("Offset owner");
        for (var i = 0; i < 6; i++) await CreateSimpleFormAsync(owner.Cookie, $"Offset {i}");
        using var response = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms?page={page}&pageSize={size}", owner.Cookie);
        Console.WriteLine($"F01 page={page}, size={size}, old offset={unchecked((page - 1) * size)}, status={response.StatusCode}, body={await response.Content.ReadAsStringAsync()}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.AreEqual(0, json.RootElement.GetProperty("items").GetArrayLength());
        var pagination = json.RootElement.GetProperty("pagination");
        Assert.AreEqual(page, pagination.GetProperty("page").GetInt32());
        Assert.AreEqual(size, pagination.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(6, pagination.GetProperty("totalItems").GetInt32());
        Assert.AreEqual(size == 4 ? 2 : 1, pagination.GetProperty("totalPages").GetInt32());
    }

    [TestMethod]
    [DataRow("register", "displayName", "high")]
    [DataRow("register", "displayName", "low")]
    [DataRow("register", "key", "high")]
    [DataRow("login", "password", "high")]
    [DataRow("login", "key", "high")]
    [DataRow("user", "displayName", "high")]
    [DataRow("user", "key", "high")]
    public async Task Repair_F02_auth_user_string_and_key_decode(string endpoint, string field, string surrogate)
    {
        var owner = await CreateMemberAsync("Unicode owner");
        var email = $"repair-{Guid.NewGuid():N}@example.test";
        _emailsToDelete.Add(email);
        var escape = surrogate == "high" ? "\\uD800" : "\\uDC00";
        var body = endpoint switch
        {
            "register" => $$"""{"email":"{{email}}","password":"valid repair password","displayName":"{{escape}}"} """,
            "login" => $$"""{"email":"{{owner.Email}}","password":"{{escape}}"}""",
            _ => $$"""{"displayName":"{{escape}}"}"""
        };
        if (field == "key") body = $$"""{"{{escape}}":0}""";
        var before = await ScalarAsync<string>("SELECT row_to_json(u)::text FROM users u WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        using var response = await RepairSendAsync(endpoint == "user" ? HttpMethod.Patch : HttpMethod.Post,
            endpoint == "user" ? "/api/v1/users/me" : $"/api/v1/auth/{endpoint}", endpoint == "user" ? owner.Cookie : "", body);
        await RepairAssertDetailAsync(response, "VALIDATION_ERROR", field == "key" ? "body" : field, "INVALID_VALUE");
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
        Assert.AreEqual(before, await ScalarAsync<string>("SELECT row_to_json(u)::text FROM users u WHERE id=@id", new NpgsqlParameter("id", owner.Id)));
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT count(*) FROM users WHERE email=@email", new NpgsqlParameter("email", email)));
    }

    [TestMethod]
    [DataRow("create", "title")]
    [DataRow("create", "description")]
    [DataRow("create", "responseMode")]
    [DataRow("create", "responseIdentity")]
    [DataRow("create", "resultVisibility")]
    [DataRow("create", "submissionDeadlineAt")]
    [DataRow("create", "key")]
    [DataRow("create", "questionTitle")]
    [DataRow("create", "questionDescription")]
    [DataRow("create", "questionType")]
    [DataRow("create", "questionKey")]
    [DataRow("create", "optionLabel")]
    [DataRow("create", "optionValue")]
    [DataRow("create", "optionKey")]
    [DataRow("create", "placeholder")]
    [DataRow("create", "variant")]
    [DataRow("create", "configKey")]
    [DataRow("patch", "title")]
    [DataRow("patch", "description")]
    [DataRow("patch", "responseMode")]
    [DataRow("patch", "responseIdentity")]
    [DataRow("patch", "submissionDeadlineAt")]
    [DataRow("patch", "key")]
    [DataRow("patch", "questionTitle")]
    [DataRow("patch", "questionDescription")]
    [DataRow("patch", "questionType")]
    [DataRow("patch", "questionKey")]
    [DataRow("patch", "questionId")]
    [DataRow("patch", "optionLabel")]
    [DataRow("patch", "optionValue")]
    [DataRow("patch", "optionKey")]
    [DataRow("patch", "optionId")]
    [DataRow("patch", "placeholder")]
    [DataRow("patch", "variant")]
    [DataRow("patch", "configKey")]
    [DataRow("visibility", "resultVisibility")]
    [DataRow("visibility", "key")]
    public async Task Repair_F02_form_decoding_preserves_layer_code_and_path(string action, string field)
    {
        var owner = await CreateMemberAsync("Definition unicode owner");
        var id = await CreateNestedFormAsync(owner.Cookie, "Before");
        using var original = await GetFormJsonAsync(id, owner.Cookie);
        var question = original.RootElement.GetProperty("questions")[0];
        var qid = question.GetProperty("id").GetString();
        var oid = question.GetProperty("options")[0].GetProperty("id").GetString();
        var (body, path, detail, nested) = RepairUnicodeBody(field);
        body = body.Replace("$Q", qid).Replace("$O", oid);
        if (action == "visibility") body = body.Replace("\"title\":\"Good\",", "");
        var before = await ReadDefinitionRowsAsync(id);
        var count = await ScalarAsync<long>("SELECT count(*) FROM forms WHERE user_id=@id", new NpgsqlParameter("id", owner.Id));
        using var response = await RepairSendAsync(action == "create" ? HttpMethod.Post : HttpMethod.Patch,
            action == "create" ? "/api/v1/forms" : $"/api/v1/forms/{id}" + (action == "visibility" ? "/result-visibility" : ""), owner.Cookie, body);
        await RepairAssertDetailAsync(response, nested ? "FORM_DEFINITION_INVALID" : "VALIDATION_ERROR", path, detail);
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
        Assert.AreEqual(count, await ScalarAsync<long>("SELECT count(*) FROM forms WHERE user_id=@id", new NpgsqlParameter("id", owner.Id)));
    }

    private static (string Body, string Path, string Detail, bool Nested) RepairUnicodeBody(string field)
    {
        const string bad = "\\uD800";
        var basic = field switch
        {
            "key" => ($$"""{"{{bad}}":0}""", "body"),
            "title" => ($$"""{"title":"{{bad}}"}""", "title"),
            _ => ($$"""{"title":"Good","{{field}}":"{{bad}}"}""", field)
        };
        if (!field.StartsWith("question", StringComparison.Ordinal) && !field.StartsWith("option", StringComparison.Ordinal)
            && field is not ("placeholder" or "variant" or "configKey"))
            return (basic.Item1, basic.Item2, "INVALID_VALUE", false);
        var q = "\"type\":\"single_choice\",\"title\":\"Good\",\"sortOrder\":0";
        var path = "questions[0]";
        var detail = "INVALID_VALUE";
        switch (field)
        {
            case "questionTitle": q = q.Replace("\"Good\"", $"\"{bad}\""); path += ".title"; break;
            case "questionDescription": q += $",\"description\":\"{bad}\""; path += ".description"; break;
            case "questionType": q = q.Replace("single_choice", bad); path += ".type"; detail = "INVALID_QUESTION_TYPE"; break;
            case "questionId": q += $",\"id\":\"{bad}\""; path += ".id"; break;
            case "questionKey": q += $",\"{bad}\":0"; break;
            case "optionLabel": q += $$""", "options":[{"label":"{{bad}}","value":"one","sortOrder":0}]"""; path += ".options[0].label"; break;
            case "optionValue": q += $$""", "options":[{"label":"Good","value":"{{bad}}","sortOrder":0}]"""; path += ".options[0].value"; break;
            case "optionKey": q += $$""", "options":[{"{{bad}}":0}]"""; path += ".options[0]"; break;
            case "optionId": q += $$""", "id":"$Q","options":[{"id":"{{bad}}"}]"""; path += ".options[0].id"; break;
            case "placeholder": q = q.Replace("single_choice", "text") + $$""", "config":{"placeholder":"{{bad}}"}"""; path += ".config.placeholder"; detail = "INVALID_QUESTION_CONFIG"; break;
            case "variant": q += $$""", "config":{"variant":"{{bad}}"}"""; path += ".config.variant"; detail = "INVALID_QUESTION_CONFIG"; break;
            case "configKey": q += $$""", "config":{"{{bad}}":0}"""; path += ".config"; detail = "INVALID_QUESTION_CONFIG"; break;
        }
        return ($$"""{"title":"Good","questions":[{ {{q}} }]}""", path, detail, true);
    }

    [TestMethod]
    public async Task Repair_F02_shape_resource_owner_lifecycle_priority_and_valid_pair()
    {
        var owner = await CreateMemberAsync("Priority owner");
        var other = await CreateMemberAsync("Priority other");
        var id = await CreateNestedFormAsync(owner.Cookie, "Before");
        var before = await ReadDefinitionRowsAsync(id);
        using var auth = await RepairSendAsync(HttpMethod.Post, "/api/v1/auth/register", "", "{\"unknown\":0,\"displayName\":\"\\uD800\"}");
        await RepairAssertDetailAsync(auth, "VALIDATION_ERROR", "unknown", "INVALID_VALUE");
        using var create = await RepairSendAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie, "{\"title\":0,\"questions\":[{\"title\":\"\\uD800\"}]}");
        await RepairAssertDetailAsync(create, "VALIDATION_ERROR", "title", "INVALID_VALUE");
        foreach (var body in new[] { "{\"title\":\"\\uD800\"}", "{\"\\uD800\":0}", "{\"questions\":[{\"id\":\"\\uD800\"}]}" })
        {
            await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{Guid.NewGuid()}", owner.Cookie, body), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
            await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{id}", other.Cookie, body), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        }
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
        await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{Guid.NewGuid()}/result-visibility", owner.Cookie, "{\"resultVisibility\":\"\\uD800\"}"), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility", other.Cookie, "{\"resultVisibility\":\"\\uD800\"}"), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{id}/publish", owner.Cookie);
        foreach (var state in new[] { "published", "closed" })
        {
            foreach (var body in new[] { "{\"title\":\"\\uD800\"}", "{\"\\uD800\":0}" })
            {
                await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{id}", owner.Cookie, body), HttpStatusCode.Conflict, "FORM_NOT_EDITABLE");
                await AssertErrorAsync(await RepairSendAsync(HttpMethod.Patch, $"/api/v1/forms/{id}", other.Cookie, body), HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
            }
            if (state == "published") await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{id}/close", owner.Cookie);
        }
        using var pair = await RepairSendAsync(HttpMethod.Patch, "/api/v1/users/me", owner.Cookie, "{\"display\\u004Eame\":\"  \\uD83D\\uDE00  \"}");
        Assert.AreEqual(HttpStatusCode.OK, pair.StatusCode);
        using var pairJson = await ReadJsonAsync(pair);
        Assert.AreEqual("😀", pairJson.RootElement.GetProperty("displayName").GetString());
    }

    [TestMethod]
    [DataRow("register")]
    [DataRow("login")]
    [DataRow("user")]
    [DataRow("create")]
    [DataRow("patch")]
    [DataRow("visibility")]
    [DataRow("submit")]
    public async Task Repair_F03_unsupported_media_type_uses_contract(string action)
    {
        var owner = await CreateMemberAsync("Media owner");
        var id = await CreateNestedFormAsync(owner.Cookie, "Before");
        var before = await ReadDefinitionRowsAsync(id);
        var (method, path) = RepairRoute(action, id.ToString());
        using var response = await RepairSendAsync(method, path, action is "register" or "login" ? "" : owner.Cookie, "{}", "text/plain");
        await RepairAssertDetailAsync(response, "VALIDATION_ERROR", "body", "INVALID_VALUE");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow("user", "value")]
    [DataRow("user", "key")]
    [DataRow("user", "media")]
    [DataRow("create", "key")]
    [DataRow("create", "media")]
    [DataRow("patch", "value")]
    [DataRow("patch", "key")]
    [DataRow("patch", "media")]
    [DataRow("visibility", "value")]
    [DataRow("visibility", "media")]
    [DataRow("submit", "key")]
    [DataRow("submit", "media")]
    public async Task Repair_authentication_precedes_bad_path_body_and_media(string action, string failure)
    {
        var owner = await CreateMemberAsync("Auth priority owner");
        var body = failure == "value" ? "{\"displayName\":\"\\uD800\",\"resultVisibility\":\"\\uD800\"}" : "{\"\\uD800\":0}";
        var (method, path) = RepairRoute(action, "not-a-uuid");
        var cookies = new[] { "", "access_token=invalid", R9TokenCookie(owner.Id, DateTime.UtcNow.AddHours(-1)), R9TokenCookie(Guid.NewGuid(), DateTime.UtcNow.AddHours(1)), owner.Cookie };
        var codes = new[] { "AUTH_REQUIRED", "AUTH_TOKEN_INVALID", "AUTH_TOKEN_EXPIRED", "AUTH_TOKEN_INVALID", "AUTH_USER_DISABLED" };
        for (var i = 0; i < cookies.Length; i++)
        {
            if (i == 4) await ExecuteAsync("UPDATE users SET status='disabled' WHERE id=@id", new NpgsqlParameter("id", owner.Id));
            using var response = await RepairSendAsync(method, path, cookies[i], body, failure == "media" ? "text/plain" : "application/json");
            Console.WriteLine($"Auth priority: {action}/{failure}, {codes[i]}, actual={response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
            await AssertErrorAsync(response, i == 4 ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, codes[i]);
        }
    }

    [TestMethod]
    public async Task Repair_unexpected_InvalidOperationException_remains_server_error()
    {
        var fault = new RepairReadFault();
        await UseR8FactoryAsync(interceptors: [fault]);
        var owner = await CreateMemberAsync("Server fault owner");
        var id = await CreateNestedFormAsync(owner.Cookie, "Before");
        fault.FormId = id;
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{id}", owner.Cookie), HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
    }

    [TestMethod]
    [DataRow("root", false)]
    [DataRow("questions", false)]
    [DataRow("root", true)]
    public async Task Repair_S01_detail_split_query_returns_one_complete_version(string stopAfter, bool publish)
    {
        var gate = new RepairDetailGate(stopAfter);
        await UseR8FactoryAsync(interceptors: [gate]);
        var owner = await CreateMemberAsync("Snapshot owner");
        using var create = await RepairSendAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie, """
            {"title":"V0 form","description":"V0 description","questions":[{"type":"single_choice","title":"V0 question","sortOrder":0,"config":{"variant":"radio"},"options":[{"label":"V0 A","value":"a","sortOrder":0},{"label":"V0 B","value":"b","sortOrder":1}]}]}
            """);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadJsonAsync(create);
        var id = created.RootElement.GetProperty("id").GetGuid();
        // Compare persisted timestamp precision, not the pre-round-trip Create DTO.
        using var v0 = await GetFormJsonAsync(id, owner.Cookie);
        var q = v0.RootElement.GetProperty("questions")[0];
        var qid = q.GetProperty("id").GetGuid();
        var o = q.GetProperty("options");
        gate.FormId = id;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var read = R8RequestAsync(HttpMethod.Get, $"/api/v1/forms/{id}", owner.Cookie, null, cancellation.Token);
        var workers = new List<Task<HttpResponseMessage>> { read };
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(stopAfter == "root" ? 1 : 2, gate.ReadCount);
            var patchRequest = R8RequestAsync(HttpMethod.Patch, $"/api/v1/forms/{id}", owner.Cookie, R7Json($$"""
                {"title":"V1 form","description":"V1 description","questions":[{"id":"{{qid}}","title":"V1 question","config":{"variant":"select"},"options":[{"id":"{{o[0].GetProperty("id").GetGuid()}}","label":"V1 A","sortOrder":1},{"id":"{{o[1].GetProperty("id").GetGuid()}}"},{"label":"V1 C","value":"c","sortOrder":0}]}]}
                """), cancellation.Token);
            workers.Add(patchRequest);
            using var patch = await patchRequest;
            Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
            using var v1 = await ReadJsonAsync(patch);
            var committedRows = await ReadDefinitionRowsAsync(id);
            StringAssert.Contains(committedRows, "V1 form");
            StringAssert.Contains(committedRows, "V1 A");
            if (publish)
            {
                var publishRequest = R8RequestAsync(HttpMethod.Post, $"/api/v1/forms/{id}/publish", owner.Cookie, null, cancellation.Token);
                workers.Add(publishRequest);
                using var published = await publishRequest;
                Assert.AreEqual(HttpStatusCode.OK, published.StatusCode, await published.Content.ReadAsStringAsync());
                using var p = await ReadJsonAsync(published);
                Assert.AreEqual("published", p.RootElement.GetProperty("status").GetString());
            }
            var afterWriter = await ReadDefinitionRowsAsync(id);
            Assert.IsFalse(read.IsCompleted, "Writer must commit while this GET is paused.");
            Assert.AreEqual(stopAfter == "root" ? 1 : 2, gate.ReadCount, "No later split SELECT may execute before release.");
            Console.WriteLine($"S01 {stopAfter}, publish={publish}: GET context={gate.ContextId}, isolation={gate.Isolation}, completed SELECTs={gate.ReadCount}; Patch committed and independently read back before release.");
            Console.WriteLine(string.Join("\n", gate.Sql));
            gate.Release.TrySetResult();
            using var result = await read.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            using var actual = await ReadJsonAsync(result);
            Assert.AreEqual(3, gate.ReadCount, "All three split SELECTs must use the same GET context.");
            Console.WriteLine(string.Join("\n", gate.Sql));
            Console.WriteLine($"S01 V0={v0.RootElement.GetRawText()}\nV1={v1.RootElement.GetRawText()}\nActual={actual.RootElement.GetRawText()}");
            using var fresh = await GetFormJsonAsync(id, owner.Cookie);
            Assert.AreEqual(publish ? "published" : "draft", fresh.RootElement.GetProperty("status").GetString());
            Assert.IsTrue(JsonElement.DeepEquals(v1.RootElement.GetProperty("questions"), fresh.RootElement.GetProperty("questions")));
            Assert.AreEqual("V1 form", fresh.RootElement.GetProperty("title").GetString());
            if (!publish) Assert.IsTrue(JsonElement.DeepEquals(v1.RootElement, fresh.RootElement), "Next GET must return the complete committed V1.");
            Assert.AreEqual(afterWriter, await ReadDefinitionRowsAsync(id), "GET must not write business rows.");
            Assert.IsTrue(JsonElement.DeepEquals(v0.RootElement, actual.RootElement), "Established GET snapshot must return the complete V0, never mixed Definition versions.");
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
            cancellation.Cancel();
            await DrainR8RequestsAsync([.. workers]);
        }
    }

    private static (HttpMethod Method, string Path) RepairRoute(string action, string id) => action switch
    {
        "register" or "login" => (HttpMethod.Post, $"/api/v1/auth/{action}"),
        "user" => (HttpMethod.Patch, "/api/v1/users/me"),
        "create" => (HttpMethod.Post, "/api/v1/forms"),
        "visibility" => (HttpMethod.Patch, $"/api/v1/forms/{id}/result-visibility"),
        "submit" => (HttpMethod.Post, $"/api/v1/forms/{id}/responses"),
        _ => (HttpMethod.Patch, $"/api/v1/forms/{id}")
    };

    private async Task<HttpResponseMessage> RepairSendAsync(HttpMethod method, string path, string cookie, string body, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(body, Encoding.UTF8, contentType) };
        if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request);
    }

    private static async Task RepairAssertDetailAsync(HttpResponseMessage response, string code, string path, string detail)
    {
        Console.WriteLine($"Repair expected={code}/{path}/{detail}; actual={response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
        using var body = await ReadJsonAsync(response);
        Assert.IsTrue(body.RootElement.GetProperty("error").GetProperty("details").EnumerateArray().Any(d =>
            d.GetProperty("path").GetString() == path && d.GetProperty("code").GetString() == detail), $"Missing {path}/{detail}");
    }

    private sealed class RepairReadFault : DbCommandInterceptor
    {
        public Guid? FormId { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (FormId is { } id && command.CommandText.Contains("FROM forms AS", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid value && value == id))
                throw new InvalidOperationException("Controlled unexpected server failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RepairDetailGate(string stopAfter) : DbCommandInterceptor
    {
        public Guid? FormId { get; set; }
        public Guid? ContextId { get; private set; }
        public string? Isolation { get; private set; }
        public int ReadCount { get; private set; }
        public ConcurrentQueue<string> Sql { get; } = new();
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (FormId is not { } id || !command.CommandText.Contains("FROM forms AS", StringComparison.Ordinal)
                || !command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid value && value == id)) return result;
            ContextId ??= eventData.Context!.ContextId.InstanceId;
            if (eventData.Context!.ContextId.InstanceId != ContextId) return result;
            var stage = command.CommandText.Contains("JOIN options AS", StringComparison.Ordinal) ? "options"
                : command.CommandText.Contains("JOIN questions AS", StringComparison.Ordinal) ? "questions" : "root";
            ReadCount++;
            Sql.Enqueue($"S01 SELECT executed: {stage}, context={ContextId}, SQL={command.CommandText}");
            if (stage == stopAfter && !Reached.Task.IsCompleted)
            {
                Isolation = command.Transaction?.IsolationLevel.ToString() ?? "no transaction";
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
