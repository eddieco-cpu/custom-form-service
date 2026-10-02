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
