using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    [DataRow("draft", "owner_only")]
    [DataRow("draft", "respondents")]
    [DataRow("draft", "public")]
    [DataRow("published", "owner_only")]
    [DataRow("published", "respondents")]
    [DataRow("published", "public")]
    [DataRow("closed", "owner_only")]
    [DataRow("closed", "respondents")]
    [DataRow("closed", "public")]
    public async Task R10_LifecycleVisibilityAndCredentialPriority(string state, string visibility)
    {
        var owner = await CreateMemberAsync("Statistics Owner");
        var respondent = await CreateMemberAsync("Statistics Respondent");
        var other = await CreateMemberAsync("Statistics Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, respondent.Cookie, "{\"answers\":[]}")).StatusCode);
        // Draft fixture has an existing response only to keep caller roles identical in the matrix.
        await ExecuteAsync("UPDATE forms SET status=@state::form_status, result_visibility=@visibility::result_visibility, submission_deadline_at=now()-interval '1 day' WHERE id=@id",
            new NpgsqlParameter("state", state), new NpgsqlParameter("visibility", visibility), new NpgsqlParameter("id", form.Id));
        foreach (var caller in new[] { ("owner", owner.Cookie), ("respondent", respondent.Cookie), ("other", other.Cookie), ("anonymous", ""), ("invalid", "access_token=broken") })
        {
            var response = await R10GetAsync(form.Id, caller.Item2);
            if (state == "draft")
                await AssertErrorAsync(response, caller.Item1 == "owner" ? HttpStatusCode.Conflict : HttpStatusCode.NotFound,
                    caller.Item1 == "owner" ? "FORM_STATISTICS_NOT_AVAILABLE" : "FORM_NOT_FOUND");
            else if (state == "published")
                await AssertErrorAsync(response, caller.Item1 is "anonymous" or "invalid" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
                    caller.Item1 is "anonymous" or "invalid" ? "FORM_NOT_FOUND" : "FORM_STATISTICS_NOT_AVAILABLE");
            else if (visibility == "public" || caller.Item1 == "owner" || (visibility == "respondents" && caller.Item1 == "respondent"))
            {
                using var json = await R10SuccessAsync(response);
                R10RootShape(json.RootElement);
                Assert.AreEqual(visibility, json.RootElement.GetProperty("resultVisibility").GetString());
                Assert.AreEqual(1, json.RootElement.GetProperty("count").GetInt32());
            }
            else
                await AssertErrorAsync(response, caller.Item1 is "anonymous" or "invalid" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                    caller.Item1 == "anonymous" ? "AUTH_REQUIRED" : caller.Item1 == "invalid" ? "AUTH_TOKEN_INVALID" : "STATISTICS_ACCESS_DENIED");
            if (response.StatusCode != HttpStatusCode.OK) await R10ErrorShapeAsync(response);
        }
        await AssertErrorAsync(await R10GetPathAsync("invalid", "access_token=broken"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using (var invalid = await ReadJsonAsync(await R10GetPathAsync("invalid", "access_token=broken")))
            Assert.AreEqual("formId", invalid.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("path").GetString());
        await AssertErrorAsync(await R10GetAsync(Guid.NewGuid(), "access_token=broken"), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        await ExecuteAsync("UPDATE forms SET deleted_at=now() WHERE id=@id", new NpgsqlParameter("id", form.Id));
        await AssertErrorAsync(await R10GetAsync(form.Id, "access_token=broken"), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
    }

    [TestMethod]
    [DataRow("missing", "AUTH_REQUIRED", 401)]
    // ASP.NET Request.Cookies drops a literal empty cookie; match the existing protected route.
    [DataRow("empty", "AUTH_REQUIRED", 401)]
    [DataRow("malformed", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("issuer", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("audience", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("signature", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("algorithm", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("sub", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("no-expiry", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("not-yet-valid", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("expired", "AUTH_TOKEN_EXPIRED", 401)]
    [DataRow("absent-user", "AUTH_TOKEN_INVALID", 401)]
    [DataRow("disabled", "AUTH_USER_DISABLED", 403)]
    public async Task R10_ConditionalAuthUsesExistingJwtPolicy(string kind, string code, int status)
    {
        var key = kind == "algorithm" ? new string('K', 64) : CustomFormServiceApiFactory.JwtSigningKey;
        if (kind == "algorithm")
        {
            _client.Dispose();
            await _factory.DisposeAsync();
            _factory = new CustomFormServiceApiFactory(overrides: new Dictionary<string, string?> { ["Jwt:SigningKey"] = key });
            _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        }
        var owner = await CreateMemberAsync("Auth policy owner");
        var form = await CreateR8FormAsync(owner.Cookie);
        var cookie = R10Cookie(kind, owner.Id, key);
        if (kind == "disabled") await ExecuteAsync("UPDATE users SET status='disabled' WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        await ExecuteAsync("UPDATE forms SET status='closed' WHERE id=@id", new NpgsqlParameter("id", form.Id));
        await AssertErrorAsync(await R10GetAsync(form.Id, cookie), (HttpStatusCode)status, code);
        // The ordinary protected route is the policy reference, not a second invented validator.
        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        if (cookie.Length > 0) protectedRequest.Headers.Add("Cookie", cookie);
        await AssertErrorAsync(await _client.SendAsync(protectedRequest), (HttpStatusCode)status, code);
        foreach (var state in new[] { "draft", "published" })
        {
            await ExecuteAsync("UPDATE forms SET status=@state::form_status WHERE id=@id", new NpgsqlParameter("state", state), new NpgsqlParameter("id", form.Id));
            await AssertErrorAsync(await R10GetAsync(form.Id, cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        }
        await ExecuteAsync("UPDATE forms SET status='closed', result_visibility='public' WHERE id=@id", new NpgsqlParameter("id", form.Id));
        using var json = await R10SuccessAsync(await R10GetAsync(form.Id, cookie));
        R10RootShape(json.RootElement);
    }

    [TestMethod]
    public async Task R10_BearerHeaderDoesNotAuthenticate()
    {
        var owner = await CreateMemberAsync("Header owner");
        var form = await CreateR8FormAsync(owner.Cookie);
        await R10CloseAsync(form, owner.Cookie);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/forms/{form.Id}/statistics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Cookie["access_token=".Length..]);
        await AssertErrorAsync(await _client.SendAsync(request), HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
    }

    [TestMethod]
    [DataRow("owner_only", "anonymous", "single")]
    [DataRow("respondents", "anonymous", "multiple")]
    [DataRow("public", "anonymous", "single")]
    [DataRow("owner_only", "identified", "multiple")]
    [DataRow("respondents", "identified", "single")]
    [DataRow("public", "identified", "multiple")]
    public async Task R10_ZeroDataRootAndPrivacyShapes(string visibility, string identity, string mode)
    {
        var owner = await CreateMemberAsync("Original Creator");
        var form = await CreateR8FormAsync(owner.Cookie, mode);
        await ExecuteAsync("UPDATE forms SET result_visibility=@visibility::result_visibility, response_identity=@identity::response_identity WHERE id=@id",
            new NpgsqlParameter("visibility", visibility), new NpgsqlParameter("identity", identity), new NpgsqlParameter("id", form.Id));
        await R10CloseAsync(form, owner.Cookie);
        await ExecuteAsync("UPDATE users SET display_name='Current Creator' WHERE id=@id", new NpgsqlParameter("id", owner.Id));
        using var json = await R10SuccessAsync(await R10GetAsync(form.Id, owner.Cookie));
        var root = json.RootElement;
        R10RootShape(root);
        Assert.AreEqual("Current Creator", root.GetProperty("creatorDisplayName").GetString());
        Assert.AreEqual(mode, root.GetProperty("responseMode").GetString());
        Assert.AreEqual(visibility, root.GetProperty("resultVisibility").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("submissionDeadlineAt").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("description").ValueKind);
        Assert.AreEqual(0, root.GetProperty("count").GetInt32());
        var questions = root.GetProperty("questions").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(form.Questions, questions.Select(question => question.GetProperty("questionId").GetGuid()).ToArray());
        var lengths = new[] { 0, 0, 1, 2, 11 };
        for (var index = 0; index < questions.Length; index++)
        {
            R10Properties(questions[index], "questionId", "title", "description", "type", "count", "statistics");
            Assert.AreEqual(JsonValueKind.Null, questions[index].GetProperty("description").ValueKind);
            Assert.AreEqual(0, questions[index].GetProperty("count").GetInt32());
            Assert.HasCount(lengths[index], questions[index].GetProperty("statistics").EnumerateArray().ToArray());
            foreach (var item in questions[index].GetProperty("statistics").EnumerateArray())
            {
                R10ItemShape(item, index is 2 or 3, identity == "identified");
                Assert.AreEqual(0, item.GetProperty("count").GetInt32());
            }
        }
        if (visibility == "public")
        {
            await ExecuteAsync("UPDATE users SET status='disabled' WHERE id=@id", new NpgsqlParameter("id", owner.Id));
            using var disabledCreator = await R10SuccessAsync(await R10GetAsync(form.Id));
            Assert.AreEqual("Current Creator", disabledCreator.RootElement.GetProperty("creatorDisplayName").GetString());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task R10_FiveTypesCountsOrderingAndIdentity(bool identified)
    {
        var capture = new SqlCaptureInterceptor();
        await UseR8FactoryAsync(interceptors: [capture]);
        var owner = await CreateMemberAsync("Creator");
        var form = await CreateR8FormAsync(owner.Cookie, "multiple");
        var members = new List<TestMember>();
        var texts = new[] { "b", "A", "a", "A", "😀" };
        var numbers = new[] { "1", "1.0", "1e0", "-0", "0" };
        var ratings = new[] { 5, 5, 0, 10, 5 };
        for (var index = 0; index < texts.Length; index++)
        {
            var member = await CreateMemberAsync(index < 2 ? "Same" : $"Name {index}");
            members.Add(member);
            Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, member.Cookie, $$"""
                {"answers":[{"questionId":"{{form.Questions[0]}}","value":"  {{texts[index]}}  "},
                {"questionId":"{{form.Questions[1]}}","value":{{numbers[index]}}},
                {"questionId":"{{form.Questions[2]}}","value":"{{form.Options[0]}}"},
                {"questionId":"{{form.Questions[3]}}","value":["{{form.Options[1]}}","{{form.Options[2]}}"]},
                {"questionId":"{{form.Questions[4]}}","value":{{ratings[index]}}}]}
                """)).StatusCode);
        }
        // Owner's latest whole response erases all their older answers, but retains root membership.
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}")).StatusCode);
        await ExecuteAsync("UPDATE users SET display_name='Renamed', status='disabled' WHERE id=@id", new NpgsqlParameter("id", members[4].Id));
        await ExecuteAsync("UPDATE forms SET response_identity=@identity::response_identity, result_visibility='public' WHERE id=@id",
            new NpgsqlParameter("identity", identified ? "identified" : "anonymous"), new NpgsqlParameter("id", form.Id));
        await R10CloseAsync(form, owner.Cookie);
        var before = await R9ReadRowsAsync(form.Id);
        capture.Clear();
        using var json = await R10SuccessAsync(await R10GetAsync(form.Id, owner.Cookie));
        var root = json.RootElement;
        R10RootShape(root);
        Assert.AreEqual(6, root.GetProperty("count").GetInt32());
        var questions = root.GetProperty("questions").EnumerateArray().ToArray();
        Assert.IsTrue(questions.All(question => question.GetProperty("count").GetInt32() == 5));
        CollectionAssert.AreEqual(new[] { "A", "a", "b", "😀" }, questions[0].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("value").GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 1 }, questions[0].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("count").GetInt32()).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, questions[1].GetProperty("statistics").EnumerateArray().Select(item => (int)item.GetProperty("value").GetDecimal()).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 3 }, questions[1].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("count").GetInt32()).ToArray());
        Assert.AreEqual("one", questions[2].GetProperty("statistics")[0].GetProperty("value").GetString());
        Assert.AreEqual(form.Options[0], questions[2].GetProperty("statistics")[0].GetProperty("optionId").GetGuid());
        CollectionAssert.AreEqual(new[] { "one", "two" }, questions[3].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("value").GetString()).ToArray());
        Assert.AreEqual(10, questions[3].GetProperty("statistics").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()));
        CollectionAssert.AreEqual(Enumerable.Range(0, 11).ToArray(), questions[4].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("value").GetInt32()).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 0, 0, 0, 3, 0, 0, 0, 0, 1 }, questions[4].GetProperty("statistics").EnumerateArray().Select(item => item.GetProperty("count").GetInt32()).ToArray());
        var names = members.ToDictionary(member => member.Id, member => member == members[4] ? "Renamed" : members.IndexOf(member) < 2 ? "Same" : $"Name {members.IndexOf(member)}");
        foreach (var question in questions)
        {
            R10Properties(question, "questionId", "title", "description", "type", "count", "statistics");
            foreach (var item in question.GetProperty("statistics").EnumerateArray())
            {
                R10ItemShape(item, question.GetProperty("type").GetString() is "single_choice" or "multiple_choice", identified);
                if (!identified) continue;
                var respondents = item.GetProperty("respondents").EnumerateArray().ToArray();
                var ids = respondents.Select(respondent => respondent.GetProperty("id").GetGuid()).ToArray();
                Assert.HasCount(ids.Distinct().Count(), ids);
                CollectionAssert.AreEqual(ids.OrderBy(id => names[id], StringComparer.Ordinal).ThenBy(id => id).ToArray(), ids);
                foreach (var respondent in respondents)
                {
                    R10Properties(respondent, "id", "displayName");
                    Assert.AreEqual(names[respondent.GetProperty("id").GetGuid()], respondent.GetProperty("displayName").GetString());
                }
            }
        }
        Assert.AreEqual(before, await R9ReadRowsAsync(form.Id), "GET must not write any form/definition/response/answer row or timestamp.");
        var effectiveSql = capture.Commands.Single(sql => sql.Contains("DISTINCT ON (user_id)", StringComparison.Ordinal));
        StringAssert.Contains(effectiveSql, "WHERE form_id = @p0");
        StringAssert.Contains(effectiveSql, "ORDER BY user_id, submitted_at DESC, id DESC");
        Assert.IsFalse(effectiveSql.Contains("answers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(capture.Commands.Any(sql => sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) || sql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) || sql.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)));
        var userQueries = capture.Commands.Count(sql => sql.Contains("FROM users AS", StringComparison.Ordinal));
        Assert.AreEqual(identified ? 2 : 1, userQueries, "Anonymous only reads Creator; Identified adds one batch profile query.");
        Console.WriteLine("R10 SQL evidence:\n" + string.Join("\n", capture.Commands));
        // Privacy is a Form policy, including when the requesting caller is the Owner.
        using var guest = await R10SuccessAsync(await R10GetAsync(form.Id, "access_token=broken"));
        Assert.IsTrue(JsonElement.DeepEquals(root, guest.RootElement));
    }

    [TestMethod]
    public async Task R10_ExactNumberGroupingAndSortingBeyondDecimalAndDouble()
    {
        var owner = await CreateMemberAsync("Exact owner");
        var form = await CreateR8FormAsync(owner.Cookie);
        var huge = "1" + new string('0', 80);
        var next = "1" + new string('0', 79) + "1";
        var tiny = "0." + new string('0', 100) + "1";
        var tokens = new[] { next, "1e0", "-" + huge, tiny, "-0", "1.0", huge, "-" + tiny, "0", "1" };
        foreach (var token in tokens)
        {
            var member = await CreateMemberAsync("Exact respondent");
            Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, member.Cookie,
                $$"""{"answers":[{"questionId":"{{form.Questions[1]}}","value":{{token}}}]}""")).StatusCode);
        }
        await R10CloseAsync(form, owner.Cookie);
        using var json = await R10SuccessAsync(await R10GetAsync(form.Id, owner.Cookie));
        var items = json.RootElement.GetProperty("questions")[1].GetProperty("statistics").EnumerateArray().ToArray();
        var expected = new[] { "-" + huge, "-" + tiny, "0", tiny, "1", huge, next };
        Assert.HasCount(expected.Length, items);
        CollectionAssert.AreEqual(new[] { 1, 1, 2, 1, 3, 1, 1 }, items.Select(item => item.GetProperty("count").GetInt32()).ToArray());
        for (var index = 0; index < items.Length; index++)
        {
            Assert.AreEqual(JsonValueKind.Number, items[index].GetProperty("value").ValueKind);
            // PostgreSQL numeric canonicalization is the exact persistence reference.
            var persisted = await ScalarAsync<string>("SELECT (@value::jsonb)::text", new NpgsqlParameter("value", expected[index]));
            // Integer trailing zeros carry magnitude; trim only a fractional part for spelling equivalence.
            var actual = R10CanonicalSpelling(items[index].GetProperty("value").GetRawText());
            Assert.AreEqual(R10CanonicalSpelling(persisted), actual);
        }
    }

    [TestMethod]
    public async Task R10_EffectiveWholeResponseTieBreakAndFormScope()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("Effective owner");
        var member = await CreateMemberAsync("Effective respondent");
        var form = await CreateR8FormAsync(owner.Cookie, "multiple");
        var other = await CreateR8FormAsync(owner.Cookie, "multiple");
        using var first = await ReadJsonAsync(await R8SubmitAsync(form, member.Cookie, R8AllAnswers(form)));
        using var second = await ReadJsonAsync(await R8SubmitAsync(form, member.Cookie,
            $$"""{"answers":[{"questionId":"{{form.Questions[1]}}","value":7}]}"""));
        var low = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        var tiedAt = clock.Now.AddSeconds(10);
        // Test-only successful history with known UUIDs verifies PostgreSQL's tie-break directly.
        await ExecuteAsync("INSERT INTO responses(id,form_id,user_id,submitted_at) VALUES(@low,@form,@user,@time),(@high,@form,@user,@time)",
            new NpgsqlParameter("low", low), new NpgsqlParameter("high", high), new NpgsqlParameter("form", form.Id), new NpgsqlParameter("user", member.Id), new NpgsqlParameter("time", tiedAt.UtcDateTime));
        await ExecuteAsync("INSERT INTO answers(id,response_id,question_id,value) VALUES(@id,@response,@question,'7'::jsonb)",
            new NpgsqlParameter("id", Guid.NewGuid()), new NpgsqlParameter("response", high), new NpgsqlParameter("question", form.Questions[1]));
        var winner = await ScalarAsync<Guid>("SELECT id FROM responses WHERE form_id=@id ORDER BY submitted_at DESC,id DESC LIMIT 1", new NpgsqlParameter("id", form.Id));
        Assert.AreEqual(high, winner);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(other, owner.Cookie, R8AllAnswers(other))).StatusCode);
        await R10CloseAsync(form, owner.Cookie);
        using (var json = await R10SuccessAsync(await R10GetAsync(form.Id, owner.Cookie)))
        {
            Assert.AreEqual(1, json.RootElement.GetProperty("count").GetInt32());
            var questions = json.RootElement.GetProperty("questions");
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 0, 0 }, questions.EnumerateArray().Select(question => question.GetProperty("count").GetInt32()).ToArray());
            Assert.AreEqual(7, questions[1].GetProperty("statistics")[0].GetProperty("value").GetInt32());
        }
        Console.WriteLine($"R10 UUID tie: submittedAt={tiedAt:O}, database winner={winner}, low={low}, high={high}; only winner's number=7.");
    }

    [TestMethod]
    [DataRow(-100)]
    [DataRow(100)]
    public async Task R10_DeadlinePreservesStoredUtcMicrosecondsAndNeverCloses(int days)
    {
        var owner = await CreateMemberAsync("Deadline creator");
        var form = await CreateR8FormAsync(owner.Cookie);
        var deadline = DateTimeOffset.Parse("2030-01-02T03:04:05.123456+08:00").AddDays(days);
        await ExecuteAsync("UPDATE forms SET submission_deadline_at=@deadline, result_visibility='public' WHERE id=@id",
            new NpgsqlParameter("deadline", deadline.UtcDateTime), new NpgsqlParameter("id", form.Id));
        await AssertErrorAsync(await R10GetAsync(form.Id, owner.Cookie), HttpStatusCode.Conflict, "FORM_STATISTICS_NOT_AVAILABLE");
        await R10CloseAsync(form, owner.Cookie);
        // Also exercise a deadline in the past after an early Close.
        if (days < 0)
        {
            deadline = DateTimeOffset.UtcNow.AddDays(-100);
            deadline = new DateTimeOffset(deadline.UtcTicks / 10 * 10, TimeSpan.Zero);
            await ExecuteAsync("UPDATE forms SET submission_deadline_at=@deadline WHERE id=@id", new NpgsqlParameter("deadline", deadline.UtcDateTime), new NpgsqlParameter("id", form.Id));
        }
        var before = await R9ReadRowsAsync(form.Id);
        using var json = await R10SuccessAsync(await R10GetAsync(form.Id));
        var value = json.RootElement.GetProperty("submissionDeadlineAt");
        Assert.AreEqual(deadline.ToUniversalTime(), value.GetDateTimeOffset());
        Assert.AreEqual(TimeSpan.Zero, value.GetDateTimeOffset().Offset);
        Assert.AreEqual(before, await R9ReadRowsAsync(form.Id));
    }

    [TestMethod]
    [DataRow(0, "null")]
    [DataRow(1, "\"not-a-number\"")]
    [DataRow(2, "\"00000000-0000-0000-0000-000000000000\"")]
    [DataRow(4, "1.5")]
    public async Task R10_InvalidPersistedAnswersReturnControlled500(int question, string invalidValue)
    {
        var owner = await CreateMemberAsync("Invalid fixture owner");
        var form = await CreateR8FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form))).StatusCode);
        await R10CloseAsync(form, owner.Cookie);
        await ExecuteAsync("UPDATE answers SET value=@value::jsonb WHERE question_id=@question", new NpgsqlParameter("value", invalidValue), new NpgsqlParameter("question", form.Questions[question]));
        var before = await R9ReadRowsAsync(form.Id);
        var response = await R10GetAsync(form.Id, owner.Cookie);
        await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        await R10ErrorShapeAsync(response);
        Assert.AreEqual(before, await R9ReadRowsAsync(form.Id));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(raw.Contains("persisted", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(raw.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("visibility")]
    [DataRow("delete")]
    [DataRow("profiles")]
    [DataRow("caller-disabled")]
    public async Task R10_AuthorizationAndOutputUseOneRealSnapshot(string mutation)
    {
        var gate = new R10SnapshotGate();
        await UseR8FactoryAsync(interceptors: [gate]);
        var owner = await CreateMemberAsync("Snapshot Creator V0");
        var respondent = await CreateMemberAsync("Snapshot Respondent V0");
        var form = await CreateR8FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.Created, (await R8SubmitAsync(form, respondent.Cookie, R8AllAnswers(form))).StatusCode);
        await ExecuteAsync("UPDATE forms SET response_identity='identified',result_visibility='respondents' WHERE id=@id", new NpgsqlParameter("id", form.Id));
        await R10CloseAsync(form, owner.Cookie);
        using var v0 = await R10SuccessAsync(await R10GetAsync(form.Id, respondent.Cookie));
        gate.FormId = form.Id;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var read = R10GetAsync(form.Id, respondent.Cookie, cancellation.Token);
        Task? write = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(IsolationLevel.RepeatableRead, gate.Commands.First().Isolation);
            write = mutation switch
            {
                "visibility" => ExecuteAsync("UPDATE forms SET result_visibility='owner_only' WHERE id=@id", new NpgsqlParameter("id", form.Id)),
                "delete" => ExecuteAsync("UPDATE forms SET deleted_at=now() WHERE id=@id", new NpgsqlParameter("id", form.Id)),
                "profiles" => ExecuteAsync("UPDATE users SET display_name=CASE WHEN id=@owner THEN 'Snapshot Creator V1' ELSE 'Snapshot Respondent V1' END WHERE id=ANY(@ids)", new NpgsqlParameter("owner", owner.Id), new NpgsqlParameter("ids", new[] { owner.Id, respondent.Id })),
                "caller-disabled" => ExecuteAsync("UPDATE users SET status='disabled' WHERE id=@id", new NpgsqlParameter("id", respondent.Id)),
                _ => throw new InvalidOperationException()
            };
            await write.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(read.IsCompleted, "Writer committed while read is paused at the executed Form SELECT.");
            gate.Release.TrySetResult();
            using var sameSnapshot = await R10SuccessAsync(await read.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(JsonElement.DeepEquals(v0.RootElement, sameSnapshot.RootElement), "Authorization, Creator and respondents must all be V0.");
            Assert.IsTrue(gate.Commands.All(command => command.ContextId == gate.Commands.First().ContextId && command.Isolation == IsolationLevel.RepeatableRead));
            Assert.IsTrue(gate.Commands.All(command => !command.Sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)));
            foreach (var command in gate.Commands)
                Console.WriteLine($"R10 snapshot mutation={mutation}, ContextId={command.ContextId}, isolation={command.Isolation}, SQL={command.Sql}");
            gate.FormId = null;
            var fresh = await R10GetAsync(form.Id, respondent.Cookie);
            if (mutation == "profiles")
            {
                using var v1 = await R10SuccessAsync(fresh);
                Assert.AreEqual("Snapshot Creator V1", v1.RootElement.GetProperty("creatorDisplayName").GetString());
                Assert.AreEqual("Snapshot Respondent V1", v1.RootElement.GetProperty("questions")[0].GetProperty("statistics")[0].GetProperty("respondents")[0].GetProperty("displayName").GetString());
                Assert.IsFalse(JsonElement.DeepEquals(v0.RootElement, v1.RootElement));
            }
            else await AssertErrorAsync(fresh, mutation == "delete" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
                mutation == "delete" ? "FORM_NOT_FOUND" : mutation == "visibility" ? "STATISTICS_ACCESS_DENIED" : "AUTH_USER_DISABLED");
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
            cancellation.Cancel();
            // Drain every worker without swallowing unexpected failures; cancellation is expected on failure cleanup.
            try { (await read.WaitAsync(TimeSpan.FromSeconds(10))).Dispose(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (write is not null) await write.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private Task<HttpResponseMessage> R10GetAsync(Guid id, string cookie = "", CancellationToken cancellationToken = default) =>
        R10GetPathAsync(id.ToString(), cookie, cancellationToken);

    private async Task<HttpResponseMessage> R10GetPathAsync(string id, string cookie, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/forms/{id}/statistics");
        if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request, cancellationToken);
    }

    private async Task R10CloseAsync(R8Form form, string cookie)
    {
        var response = await SendWithCookieAsync(HttpMethod.Post, $"/api/v1/forms/{form.Id}/close", cookie);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> R10SuccessAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private static void R10RootShape(JsonElement root) => R10Properties(root,
        "formId", "title", "description", "creatorDisplayName", "responseMode", "resultVisibility", "submissionDeadlineAt", "count", "questions");

    private static void R10Properties(JsonElement value, params string[] properties) =>
        CollectionAssert.AreEquivalent(properties, value.EnumerateObject().Select(property => property.Name).ToArray());

    private static void R10ItemShape(JsonElement item, bool choice, bool identified)
    {
        var properties = choice ? new[] { "optionId", "label", "value", "count" } : new[] { "value", "count" };
        R10Properties(item, identified ? [.. properties, "respondents"] : properties);
        if (identified) Assert.AreEqual(item.GetProperty("count").GetInt32(), item.GetProperty("respondents").GetArrayLength());
    }

    private static async Task R10ErrorShapeAsync(HttpResponseMessage response)
    {
        using var json = await ReadJsonAsync(response);
        R10Properties(json.RootElement, "error");
    }

    private static string R10CanonicalSpelling(string value) => value.Contains('.', StringComparison.Ordinal) ? value.TrimEnd('0').TrimEnd('.') : value;

    private static string R10Cookie(string kind, Guid id, string key = CustomFormServiceApiFactory.JwtSigningKey)
    {
        if (kind == "missing") return "";
        if (kind == "empty") return "access_token=";
        if (kind == "malformed") return "access_token=broken";
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            kind == "issuer" ? "wrong-issuer" : CustomFormServiceApiFactory.JwtIssuer,
            kind == "audience" ? "wrong-audience" : CustomFormServiceApiFactory.JwtAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, kind == "sub" ? "not-a-uuid" : kind == "absent-user" ? Guid.NewGuid().ToString() : id.ToString())],
            kind == "not-yet-valid" ? now.AddHours(1) : now.AddHours(-1),
            kind == "no-expiry" ? null : kind == "expired" ? now.AddSeconds(-1) : now.AddHours(2),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                kind == "signature" ? new string('x', 64) : key)),
                kind == "algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256));
        return "access_token=" + new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class R10SnapshotGate : DbCommandInterceptor
    {
        private int _entered;
        public Guid? FormId { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<(Guid? ContextId, IsolationLevel? Isolation, string Sql)> Commands { get; } = new();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (FormId is null) return result;
            Commands.Enqueue((eventData.Context?.ContextId.InstanceId, command.Transaction?.IsolationLevel, command.CommandText));
            if (command.CommandText.Contains("FROM forms AS", StringComparison.Ordinal) &&
                command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == FormId) &&
                Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
