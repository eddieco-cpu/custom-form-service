using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class AuthUserEndpointsTests
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
            await ExecuteAsync("DELETE FROM users WHERE email = @email;", new NpgsqlParameter("email", email));
        }

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestMethod]
    public async Task Register_login_profile_update_logout_flow_matches_contract()
    {
        var email = NewEmail();
        _emailsToDelete.Add(email);
        const string password = "  pass word  ";

        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"  {email.ToUpperInvariant()}  ",
            password,
            displayName = "\u2003Eddie 😀\u2003"
        });

        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode);
        Assert.IsFalse(register.Headers.TryGetValues("Set-Cookie", out _));
        var registered = await ReadJsonAsync(register);
        Assert.AreEqual(email, registered.RootElement.GetProperty("email").GetString());
        Assert.AreEqual("Eddie 😀", registered.RootElement.GetProperty("displayName").GetString());
        AssertPublicUserShape(registered.RootElement);

        var beforeLogin = await _client.GetAsync("/api/v1/users/me");
        await AssertErrorAsync(beforeLogin, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"\u2003{email.ToUpperInvariant()}\u2003",
            password
        });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var setCookie = login.Headers.GetValues("Set-Cookie").Single();
        StringAssert.Contains(setCookie, "access_token=");
        StringAssert.Contains(setCookie, "httponly", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "samesite=lax", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "path=/", StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setCookie.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        var cookie = setCookie.Split(';', 2)[0];

        var me = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/users/me", cookie);
        Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
        AssertPublicUserShape((await ReadJsonAsync(me)).RootElement);

        var patch = await SendWithCookieAsync(
            HttpMethod.Patch,
            "/api/v1/users/me",
            cookie,
            JsonContent.Create(new { displayName = "  Eddie Ko  " }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode);
        Assert.AreEqual("Eddie Ko", (await ReadJsonAsync(patch)).RootElement.GetProperty("displayName").GetString());

        var changed = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/users/me", cookie);
        Assert.AreEqual("Eddie Ko", (await ReadJsonAsync(changed)).RootElement.GetProperty("displayName").GetString());

        var logout = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/auth/logout", cookie);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.IsEmpty(await logout.Content.ReadAsByteArrayAsync());
        var clearCookie = logout.Headers.GetValues("Set-Cookie").Single();
        StringAssert.Contains(clearCookie, "access_token=", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(clearCookie, "expires=", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(clearCookie, "httponly", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(clearCookie, "samesite=lax", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(clearCookie, "path=/", StringComparison.OrdinalIgnoreCase);

        var afterLogout = await _client.GetAsync("/api/v1/users/me");
        await AssertErrorAsync(afterLogout, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
    }

    [TestMethod]
    public async Task Duplicate_email_and_wrong_password_use_contract_errors()
    {
        var email = NewEmail();
        await RegisterAsync(email, "correct password", "First");

        var duplicate = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $" {email.ToUpperInvariant()} ",
            password = "another password",
            displayName = "Second"
        });
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "USER_EMAIL_ALREADY_EXISTS");

        var wrong = await LoginAsync(email, "wrong password");
        await AssertErrorAsync(wrong, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        var missing = await LoginAsync($"missing-{Guid.NewGuid():N}@example.test", "wrong password");
        await AssertErrorAsync(missing, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
    }

    [TestMethod]
    public async Task Missing_invalid_expired_and_unknown_user_tokens_are_distinguished()
    {
        var missing = await _client.GetAsync("/api/v1/users/me");
        await AssertErrorAsync(missing, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");

        var invalid = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/users/me", "access_token=not-a-jwt");
        await AssertErrorAsync(invalid, HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");

        var expired = await SendWithCookieAsync(
            HttpMethod.Get,
            "/api/v1/users/me",
            $"access_token={CreateToken(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1))}");
        await AssertErrorAsync(expired, HttpStatusCode.Unauthorized, "AUTH_TOKEN_EXPIRED");

        var unknownUser = await SendWithCookieAsync(
            HttpMethod.Get,
            "/api/v1/users/me",
            $"access_token={CreateToken(Guid.NewGuid(), DateTime.UtcNow.AddHours(1))}");
        await AssertErrorAsync(unknownUser, HttpStatusCode.Unauthorized, "AUTH_TOKEN_INVALID");
    }

    [TestMethod]
    public async Task Authorization_bearer_transport_is_not_accepted()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            CreateToken(Guid.NewGuid(), DateTime.UtcNow.AddHours(1)));

        var response = await _client.SendAsync(request);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
    }

    [TestMethod]
    public async Task Disabled_user_is_rejected_after_valid_credentials_and_on_protected_requests()
    {
        var email = NewEmail();
        const string password = "correct password";
        await RegisterAsync(email, password, "Disabled User");
        var login = await LoginAsync(email, password);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        await ExecuteAsync(
            "UPDATE users SET status = 'disabled'::user_status WHERE email = @email;",
            new NpgsqlParameter("email", email));

        var protectedResponse = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/users/me", cookie);
        await AssertErrorAsync(protectedResponse, HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");

        var correctCredentials = await LoginAsync(email, password);
        await AssertErrorAsync(correctCredentials, HttpStatusCode.Forbidden, "AUTH_USER_DISABLED");

        var wrongCredentials = await LoginAsync(email, "wrong password");
        await AssertErrorAsync(wrongCredentials, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
    }

    [TestMethod]
    public async Task Logout_succeeds_with_missing_invalid_and_expired_tokens()
    {
        foreach (var cookie in new string?[]
                 {
                     null,
                     "access_token=invalid-token",
                     $"access_token={CreateToken(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1))}"
                 })
        {
            var response = cookie is null
                ? await _client.PostAsync("/api/v1/auth/logout", null)
                : await SendWithCookieAsync(HttpMethod.Post, "/api/v1/auth/logout", cookie);
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.IsEmpty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [TestMethod]
    public async Task Production_login_cookie_is_secure()
    {
        var email = NewEmail();
        _emailsToDelete.Add(email);
        await using var productionFactory = new CustomFormServiceApiFactory(Environments.Production);
        using var productionClient = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var register = await productionClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "valid password",
            displayName = "Production Cookie"
        });
        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode);

        var login = await productionClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "valid password"
        });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var setCookie = login.Headers.GetValues("Set-Cookie").Single();
        StringAssert.Contains(setCookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "httponly", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "samesite=lax", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task Unicode_code_point_and_password_no_trim_rules_are_enforced()
    {
        var shortPassword = await PostRegisterRawAsync(NewEmail(), string.Concat(Enumerable.Repeat("😀", 9)), "Name");
        await AssertValidationPathAsync(shortPassword, "password");

        var longPassword = await PostRegisterRawAsync(NewEmail(), new string('x', 129), "Name");
        await AssertValidationPathAsync(longPassword, "password");

        var longDisplayName = await PostRegisterRawAsync(NewEmail(), string.Concat(Enumerable.Repeat("😀", 10)), new string('x', 101));
        await AssertValidationPathAsync(longDisplayName, "displayName");

        var email = NewEmail();
        const string whitespacePassword = "          ";
        var valid = await RegisterAsync(email, whitespacePassword, "😀😀😀😀😀😀😀😀😀😀");
        Assert.AreEqual(HttpStatusCode.Created, valid.StatusCode);

        var exactLogin = await LoginAsync(email, whitespacePassword);
        Assert.AreEqual(HttpStatusCode.OK, exactLogin.StatusCode);
        var trimmedLogin = await LoginAsync(email, string.Empty);
        await AssertErrorAsync(trimmedLogin, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task Unknown_readonly_null_and_malformed_fields_use_validation_envelope()
    {
        var unknown = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = NewEmail(),
            password = "valid password",
            displayName = "Name",
            role = "admin"
        });
        await AssertValidationPathAsync(unknown, "role");

        var nullName = await PostRegisterRawAsync(NewEmail(), "valid password", null);
        await AssertValidationPathAsync(nullName, "displayName");

        using var malformedContent = new StringContent("{", Encoding.UTF8, "application/json");
        var malformed = await _client.PostAsync("/api/v1/auth/register", malformedContent);
        await AssertErrorAsync(malformed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var email = NewEmail();
        await RegisterAsync(email, "valid password", "Name");
        var login = await LoginAsync(email, "valid password");
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        var readOnly = await SendWithCookieAsync(
            HttpMethod.Patch,
            "/api/v1/users/me",
            cookie,
            JsonContent.Create(new { email = "other@example.test" }));
        await AssertValidationPathAsync(readOnly, "email");
    }

    [TestMethod]
    public async Task Authentication_error_precedes_patch_body_validation()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new { email = "readonly@example.test" })
        });

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
    }

    [TestMethod]
    public async Task Empty_and_unchanged_patch_are_no_ops_for_updated_at()
    {
        var email = NewEmail();
        await RegisterAsync(email, "valid password", "Same Name");
        var login = await LoginAsync(email, "valid password");
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        var before = await ReadUpdatedAtAsync(email);

        var empty = await SendWithCookieAsync(
            HttpMethod.Patch,
            "/api/v1/users/me",
            cookie,
            JsonContent.Create(new { }));
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);

        var unchanged = await SendWithCookieAsync(
            HttpMethod.Patch,
            "/api/v1/users/me",
            cookie,
            JsonContent.Create(new { displayName = "  Same Name  " }));
        Assert.AreEqual(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.AreEqual(before, await ReadUpdatedAtAsync(email));
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email, string password, string displayName)
    {
        _emailsToDelete.Add(email);
        return await PostRegisterRawAsync(email, password, displayName);
    }

    private Task<HttpResponseMessage> PostRegisterRawAsync(string email, string password, string? displayName)
    {
        return _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName });
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        return _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
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

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = await ReadJsonAsync(response);
        var error = body.RootElement.GetProperty("error");
        Assert.AreEqual(expectedCode, error.GetProperty("code").GetString());
        Assert.IsNotEmpty(error.GetProperty("message").GetString()!);
        Assert.IsFalse(body.RootElement.TryGetProperty("title", out _));
    }

    private static async Task AssertValidationPathAsync(HttpResponseMessage response, string expectedPath)
    {
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var body = await ReadJsonAsync(response);
        var paths = body.RootElement.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(detail => detail.GetProperty("path").GetString())
            .ToArray();
        CollectionAssert.Contains(paths, expectedPath);
    }

    private static void AssertPublicUserShape(JsonElement user)
    {
        var propertyNames = user.EnumerateObject().Select(property => property.Name).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "avatarUrl", "displayName", "email", "id" }, propertyNames);
    }

    private static string CreateToken(Guid userId, DateTime expires)
    {
        var now = DateTime.UtcNow.AddHours(-2);
        var token = new JwtSecurityToken(
            CustomFormServiceApiFactory.JwtIssuer,
            CustomFormServiceApiFactory.JwtAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            now,
            expires,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomFormServiceApiFactory.JwtSigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NewEmail() => $"r4-{Guid.NewGuid():N}@example.test";

    private static async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DateTime> ReadUpdatedAtAsync(string email)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT updated_at FROM users WHERE email = @email;", connection);
        command.Parameters.AddWithValue("email", email);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private static string BuildConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = RequiredEnvironmentVariable("PostgreSql__Host"),
        Port = int.Parse(RequiredEnvironmentVariable("PostgreSql__Port"), System.Globalization.CultureInfo.InvariantCulture),
        Database = RequiredEnvironmentVariable("PostgreSql__Database"),
        Username = RequiredEnvironmentVariable("PostgreSql__Username"),
        Password = RequiredEnvironmentVariable("PostgreSql__Password"),
        IncludeErrorDetail = false
    }.ConnectionString;

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable {name} must be configured for integration tests.");
}
