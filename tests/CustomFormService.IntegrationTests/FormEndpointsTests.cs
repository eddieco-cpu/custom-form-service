using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class FormEndpointsTests
{
    private CustomFormServiceApiFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<string> _emailsToDelete = [];

    [TestInitialize]
    public void Initialize()
    {
        _factory = new CustomFormServiceApiFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var email in _emailsToDelete)
        {
            await ExecuteAsync(
                """
                DELETE FROM questions
                WHERE form_id IN (
                    SELECT id FROM forms WHERE user_id = (SELECT id FROM users WHERE email = @email)
                );
                DELETE FROM forms WHERE user_id = (SELECT id FROM users WHERE email = @email);
                DELETE FROM users WHERE email = @email;
                """,
                new NpgsqlParameter("email", email));
        }

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestMethod]
    public async Task Create_nested_draft_validates_and_persists_atomically()
    {
        var owner = await CreateMemberAsync("R5 Owner");

        var invalid = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/forms",
            owner.Cookie,
            JsonContent.Create(new
            {
                title = "Atomic failure",
                questions = new[]
                {
                    new
                    {
                        type = "single_choice",
                        title = "Duplicate options",
                        sortOrder = 0,
                        options = new[]
                        {
                            new { label = "First", value = "same", sortOrder = 0 },
                            new { label = "Second", value = "same", sortOrder = 1 }
                        }
                    }
                }
            }));
        await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM forms WHERE user_id = @userId;", new NpgsqlParameter("userId", owner.Id)));

        var create = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/forms",
            owner.Cookie,
            JsonContent.Create(new
            {
                title = "  R5 nested form  ",
                description = " \u2003 ",
                questions = new object[]
                {
                    new
                    {
                        type = "single_choice",
                        title = "  Choose one  ",
                        description = " \u2003 ",
                        required = true,
                        sortOrder = 4,
                        config = new { variant = "radio" },
                        options = new[]
                        {
                            new { label = " Second ", value = "second", sortOrder = 1 },
                            new { label = " First ", value = "first", sortOrder = 0 }
                        }
                    },
                    new
                    {
                        type = "rating",
                        title = "Rate it",
                        sortOrder = 2,
                        config = new { min = 1 }
                    }
                }
            }));

        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(create);
        var root = body.RootElement;
        var formId = root.GetProperty("id").GetGuid();
        Assert.AreEqual(owner.Id, root.GetProperty("userId").GetGuid());
        Assert.AreEqual("R5 Owner", root.GetProperty("creatorDisplayName").GetString());
        Assert.AreEqual("R5 nested form", root.GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("description").ValueKind);
        Assert.AreEqual("draft", root.GetProperty("status").GetString());
        Assert.AreEqual("single", root.GetProperty("responseMode").GetString());
        Assert.AreEqual("anonymous", root.GetProperty("responseIdentity").GetString());
        Assert.AreEqual("owner_only", root.GetProperty("resultVisibility").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("submissionDeadlineAt").ValueKind);

        var questions = root.GetProperty("questions").EnumerateArray().ToArray();
        Assert.HasCount(2, questions);
        Assert.AreEqual("rating", questions[0].GetProperty("type").GetString());
        Assert.AreEqual(2, questions[0].GetProperty("sortOrder").GetInt32());
        Assert.AreEqual("single_choice", questions[1].GetProperty("type").GetString());
        Assert.AreEqual("Choose one", questions[1].GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, questions[1].GetProperty("description").ValueKind);
        var options = questions[1].GetProperty("options").EnumerateArray().ToArray();
        Assert.HasCount(2, options);
        Assert.AreEqual("First", options[0].GetProperty("label").GetString());
        Assert.AreEqual(0, options[0].GetProperty("sortOrder").GetInt32());

        Assert.AreEqual(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM forms WHERE id = @formId;", new NpgsqlParameter("formId", formId)));
        Assert.AreEqual(2L, await ScalarAsync<long>("SELECT COUNT(*) FROM questions WHERE form_id = @formId;", new NpgsqlParameter("formId", formId)));
        Assert.AreEqual(2L, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM options WHERE question_id IN (SELECT id FROM questions WHERE form_id = @formId);",
            new NpgsqlParameter("formId", formId)));
    }

    [TestMethod]
    public async Task List_is_owner_scoped_paginated_and_stably_sorted()
    {
        var owner = await CreateMemberAsync("R5 List Owner");
        var other = await CreateMemberAsync("R5 List Other");
        var older = await CreateSimpleFormAsync(owner.Cookie, "Older");
        var newer = await CreateSimpleFormAsync(owner.Cookie, "Newer");
        await CreateSimpleFormAsync(other.Cookie, "Other member form");

        await ExecuteAsync(
            "UPDATE forms SET updated_at = @updatedAt WHERE id = @formId;",
            new NpgsqlParameter("updatedAt", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new NpgsqlParameter("formId", older));
        await ExecuteAsync(
            "UPDATE forms SET updated_at = @updatedAt WHERE id = @formId;",
            new NpgsqlParameter("updatedAt", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new NpgsqlParameter("formId", newer));

        var pageOne = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms?page=1&pageSize=1", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, pageOne.StatusCode);
        using (var body = await ReadJsonAsync(pageOne))
        {
            var root = body.RootElement;
            Assert.AreEqual("Newer", root.GetProperty("items")[0].GetProperty("title").GetString());
            Assert.AreEqual(0, root.GetProperty("items")[0].GetProperty("count").GetInt32());
            var pagination = root.GetProperty("pagination");
            Assert.AreEqual(1, pagination.GetProperty("page").GetInt32());
            Assert.AreEqual(1, pagination.GetProperty("pageSize").GetInt32());
            Assert.AreEqual(2, pagination.GetProperty("totalItems").GetInt32());
            Assert.AreEqual(2, pagination.GetProperty("totalPages").GetInt32());
        }

        var beyondPage = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms?page=3&pageSize=1", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, beyondPage.StatusCode);
        using (var body = await ReadJsonAsync(beyondPage))
        {
            Assert.AreEqual(0, body.RootElement.GetProperty("items").GetArrayLength());
            Assert.AreEqual(3, body.RootElement.GetProperty("pagination").GetProperty("page").GetInt32());
        }

        var statusFilter = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms?status=draft", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, statusFilter.StatusCode);
        using (var body = await ReadJsonAsync(statusFilter))
        {
            Assert.AreEqual(2, body.RootElement.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        }

        var emptyOtherList = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms", other.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, emptyOtherList.StatusCode);
        using (var body = await ReadJsonAsync(emptyOtherList))
        {
            Assert.AreEqual(1, body.RootElement.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        }

        var invalidPage = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms?page=0", owner.Cookie);
        await AssertErrorAsync(invalidPage, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task Detail_hides_other_drafts_and_soft_delete_preserves_definition_rows()
    {
        var owner = await CreateMemberAsync("R5 Delete Owner");
        var other = await CreateMemberAsync("R5 Delete Other");
        var formId = await CreateNestedFormAsync(owner.Cookie, "Delete me");

        var ownerDetail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, ownerDetail.StatusCode);

        var otherDetail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", other.Cookie);
        await AssertErrorAsync(otherDetail, HttpStatusCode.NotFound, "FORM_NOT_FOUND");

        var otherDelete = await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{formId}", other.Cookie);
        await AssertErrorAsync(otherDelete, HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");

        var unauthenticated = await _client.GetAsync($"/api/v1/forms/{formId}");
        await AssertErrorAsync(unauthenticated, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");

        var delete = await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.IsEmpty(await delete.Content.ReadAsByteArrayAsync());
        Assert.IsTrue(await ScalarAsync<bool>("SELECT deleted_at IS NOT NULL FROM forms WHERE id = @formId;", new NpgsqlParameter("formId", formId)));
        Assert.AreEqual(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM questions WHERE form_id = @formId;", new NpgsqlParameter("formId", formId)));
        Assert.AreEqual(1L, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM options WHERE question_id IN (SELECT id FROM questions WHERE form_id = @formId);",
            new NpgsqlParameter("formId", formId)));

        var deletedOwnerDetail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        await AssertErrorAsync(deletedOwnerDetail, HttpStatusCode.NotFound, "FORM_NOT_FOUND");

        var list = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/forms", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        using (var listBody = await ReadJsonAsync(list))
        {
            Assert.AreEqual(0, listBody.RootElement.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        }

        var repeatDelete = await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{formId}", owner.Cookie);
        await AssertErrorAsync(repeatDelete, HttpStatusCode.NotFound, "FORM_NOT_FOUND");
    }

    [TestMethod]
    public async Task Patch_applies_draft_question_and_option_delta_with_type_changes()
    {
        var owner = await CreateMemberAsync("R6 Delta Owner");
        var create = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/forms",
            owner.Cookie,
            JsonContent.Create(new
            {
                title = "R6 base",
                description = "Base description",
                questions = new object[]
                {
                    new
                    {
                        type = "single_choice",
                        title = "Choice",
                        sortOrder = 0,
                        config = new { variant = "radio" },
                        options = new[]
                        {
                            new { label = "Keep", value = "keep", sortOrder = 0 },
                            new { label = "Remove", value = "remove", sortOrder = 1 }
                        }
                    },
                    new { type = "rating", title = "Delete", sortOrder = 1, config = new { min = 1 } }
                }
            }));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadJsonAsync(create);
        var formId = created.RootElement.GetProperty("id").GetGuid();
        var questions = created.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        var choice = questions.Single(question => question.GetProperty("type").GetString() == "single_choice");
        var removedQuestion = questions.Single(question => question.GetProperty("type").GetString() == "rating");
        var keepOption = choice.GetProperty("options")[0];
        var removeOption = choice.GetProperty("options")[1];

        var patch = await SendWithCookieAsync(
            HttpMethod.Patch,
            $"/api/v1/forms/{formId}",
            owner.Cookie,
            JsonContent.Create(new
            {
                title = "  R6 updated  ",
                description = (string?)null,
                responseMode = "multiple",
                responseIdentity = "identified",
                submissionDeadlineAt = "2027-01-02T03:04:05Z",
                questions = new object[]
                {
                    new
                    {
                        id = choice.GetProperty("id").GetGuid(),
                        type = "multiple_choice",
                        options = new object[]
                        {
                            new { id = keepOption.GetProperty("id").GetGuid(), label = "  Kept  " },
                            new { id = removeOption.GetProperty("id").GetGuid() },
                            new { label = " Added ", value = "added", sortOrder = 1 }
                        }
                    },
                    new { id = removedQuestion.GetProperty("id").GetGuid() },
                    new { type = "text", title = " New text ", sortOrder = 2, config = new { variant = "textarea", minLength = 1 } }
                }
            }));

        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(patch);
        var root = body.RootElement;
        Assert.AreEqual("R6 updated", root.GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("description").ValueKind);
        Assert.AreEqual("multiple", root.GetProperty("responseMode").GetString());
        Assert.AreEqual("identified", root.GetProperty("responseIdentity").GetString());
        Assert.AreEqual("2027-01-02T03:04:05+00:00", root.GetProperty("submissionDeadlineAt").GetString());
        var updatedQuestions = root.GetProperty("questions").EnumerateArray().ToArray();
        Assert.HasCount(2, updatedQuestions);
        var updatedChoice = updatedQuestions.Single(question => question.GetProperty("type").GetString() == "multiple_choice");
        Assert.AreEqual(0, updatedChoice.GetProperty("config").EnumerateObject().Count());
        var updatedOptions = updatedChoice.GetProperty("options").EnumerateArray().ToArray();
        Assert.HasCount(2, updatedOptions);
        Assert.AreEqual("Kept", updatedOptions[0].GetProperty("label").GetString());
        Assert.AreEqual("keep", updatedOptions[0].GetProperty("value").GetString());
        Assert.AreEqual("Added", updatedOptions[1].GetProperty("label").GetString());
        Assert.AreEqual("added", updatedOptions[1].GetProperty("value").GetString());
        Assert.AreEqual("text", updatedQuestions[1].GetProperty("type").GetString());
        Assert.AreEqual("New text", updatedQuestions[1].GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task Patch_rejects_invalid_delta_atomically_and_preserves_the_draft()
    {
        var owner = await CreateMemberAsync("R6 Atomic Owner");
        var formId = await CreateNestedFormAsync(owner.Cookie, "Atomic draft");
        var before = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        using var beforeBody = await ReadJsonAsync(before);
        var question = beforeBody.RootElement.GetProperty("questions")[0];
        var option = question.GetProperty("options")[0];
        var unchangedUpdatedAt = beforeBody.RootElement.GetProperty("updatedAt").GetString();

        var conflict = await SendWithCookieAsync(
            HttpMethod.Patch,
            $"/api/v1/forms/{formId}",
            owner.Cookie,
            JsonContent.Create(new
            {
                title = "This must not persist",
                questions = new object[]
                {
                    new
                    {
                        id = question.GetProperty("id").GetGuid(),
                        type = "text",
                        options = new[] { new { label = "Invalid", value = "invalid", sortOrder = 1 } }
                    }
                }
            }));
        await AssertErrorAsync(conflict, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        using (var conflictBody = await ReadJsonAsync(conflict))
        {
            Assert.AreEqual("QUESTION_TYPE_CONFLICT", conflictBody.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("code").GetString());
        }

        var duplicate = await SendWithCookieAsync(
            HttpMethod.Patch,
            $"/api/v1/forms/{formId}",
            owner.Cookie,
            JsonContent.Create(new
            {
                questions = new object[]
                {
                    new { id = question.GetProperty("id").GetGuid() },
                    new { id = question.GetProperty("id").GetGuid() }
                }
            }));
        await AssertErrorAsync(duplicate, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");

        var after = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, after.StatusCode);
        using var afterBody = await ReadJsonAsync(after);
        Assert.AreEqual("Atomic draft", afterBody.RootElement.GetProperty("title").GetString());
        Assert.AreEqual(unchangedUpdatedAt, afterBody.RootElement.GetProperty("updatedAt").GetString());
        var persistedQuestion = afterBody.RootElement.GetProperty("questions")[0];
        Assert.AreEqual("single_choice", persistedQuestion.GetProperty("type").GetString());
        Assert.HasCount(1, persistedQuestion.GetProperty("options").EnumerateArray().ToArray());
        Assert.AreEqual(option.GetProperty("id").GetGuid(), persistedQuestion.GetProperty("options")[0].GetProperty("id").GetGuid());
    }

    [TestMethod]
    public async Task Patch_enforces_scope_lifecycle_and_noop_rules()
    {
        var owner = await CreateMemberAsync("R6 Scope Owner");
        var other = await CreateMemberAsync("R6 Scope Other");
        var ownerForm = await CreateNestedFormAsync(owner.Cookie, "Owner form");
        var otherForm = await CreateNestedFormAsync(other.Cookie, "Other form");
        var ownerDetail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{ownerForm}", owner.Cookie);
        using var ownerBody = await ReadJsonAsync(ownerDetail);
        var ownerQuestionId = ownerBody.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var initialUpdatedAt = ownerBody.RootElement.GetProperty("updatedAt").GetString();
        var otherDetail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{otherForm}", other.Cookie);
        using var otherBody = await ReadJsonAsync(otherDetail);
        var otherQuestionId = otherBody.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var otherOptionId = otherBody.RootElement.GetProperty("questions")[0].GetProperty("options")[0].GetProperty("id").GetGuid();

        var noOp = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{ownerForm}", owner.Cookie, JsonContent.Create(new { }));
        Assert.AreEqual(HttpStatusCode.OK, noOp.StatusCode);
        using (var noOpBody = await ReadJsonAsync(noOp))
        {
            Assert.AreEqual(initialUpdatedAt, noOpBody.RootElement.GetProperty("updatedAt").GetString());
        }

        var crossQuestion = await SendWithCookieAsync(
            HttpMethod.Patch,
            $"/api/v1/forms/{ownerForm}",
            owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id = otherQuestionId, title = "Not mine" } } }));
        await AssertErrorAsync(crossQuestion, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        using (var error = await ReadJsonAsync(crossQuestion))
        {
            Assert.AreEqual("QUESTION_NOT_FOUND", error.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("code").GetString());
        }

        var crossOption = await SendWithCookieAsync(
            HttpMethod.Patch,
            $"/api/v1/forms/{ownerForm}",
            owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id = ownerQuestionId, options = new[] { new { id = otherOptionId, label = "Not mine" } } } } }));
        await AssertErrorAsync(crossOption, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        using (var error = await ReadJsonAsync(crossOption))
        {
            Assert.AreEqual("OPTION_NOT_FOUND", error.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("code").GetString());
        }

        var nonOwner = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{ownerForm}", other.Cookie, JsonContent.Create(new { title = "No" }));
        await AssertErrorAsync(nonOwner, HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");

        await ExecuteAsync(
            "UPDATE forms SET status = 'published'::form_status WHERE id = @formId;",
            new NpgsqlParameter("formId", ownerForm));
        var published = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{ownerForm}", owner.Cookie, JsonContent.Create(new { title = "No" }));
        await AssertErrorAsync(published, HttpStatusCode.Conflict, "FORM_NOT_EDITABLE");
    }

    private async Task<TestMember> CreateMemberAsync(string displayName)
    {
        var email = $"r5-{Guid.NewGuid():N}@example.test";
        _emailsToDelete.Add(email);
        const string password = "r5 integration password";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName });
        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode, await register.Content.ReadAsStringAsync());
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode, await login.Content.ReadAsStringAsync());
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        using var body = await ReadJsonAsync(login);
        return new TestMember(email, body.RootElement.GetProperty("id").GetGuid(), cookie);
    }

    private async Task<Guid> CreateSimpleFormAsync(string cookie, string title)
    {
        var response = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", cookie, JsonContent.Create(new { title }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateNestedFormAsync(string cookie, string title)
    {
        var response = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/forms",
            cookie,
            JsonContent.Create(new
            {
                title,
                questions = new[]
                {
                    new
                    {
                        type = "single_choice",
                        title = "Choice",
                        sortOrder = 0,
                        options = new[] { new { label = "One", value = "one", sortOrder = 0 } }
                    }
                }
            }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> SendWithCookieAsync(
        HttpMethod method,
        string path,
        string cookie,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(response);
        Assert.AreEqual(expectedCode, body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private string BuildConnectionString() =>
        _factory.Services.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.BuildConnectionString();

    private sealed record TestMember(string Email, Guid Id, string Cookie);
}
