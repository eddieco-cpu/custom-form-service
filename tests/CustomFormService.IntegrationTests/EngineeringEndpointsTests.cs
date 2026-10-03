using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class EngineeringEndpointsTests
{
    private CustomFormServiceApiFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new CustomFormServiceApiFactory();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Health_returns_healthy_response()
    {
        var response = await _client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.AreEqual("healthy", body?.Status);
    }

    [TestMethod]
    public async Task OpenApi_document_is_available_and_describes_health()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(body, "\"/health\"");
    }

    [TestMethod]
    public async Task Swagger_UI_is_available()
    {
        var response = await _client.GetAsync("/swagger/index.html");
        var body = await response.Content.ReadAsStringAsync();
        var configuration = await _client.GetStringAsync("/swagger/index.js");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(body, "Swagger UI", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(configuration, "/openapi/v1.json");
    }

    [TestMethod]
    public async Task Database_readiness_reports_healthy()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Healthy", body);
    }

    [TestMethod]
    public async Task Development_Cors_allows_the_configured_credentialed_origin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(
            "http://localhost:3000",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.AreEqual(
            "true",
            response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [TestMethod]
    public async Task Development_Cors_omits_allow_headers_for_an_unconfigured_origin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "http://localhost:3001");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private sealed record HealthPayload(string Status);
}

public sealed class CustomFormServiceApiFactory : WebApplicationFactory<Program>
{
    public const string JwtIssuer = "custom-form-service-tests";
    public const string JwtAudience = "custom-form-service-test-client";
    public const string JwtSigningKey = "R4-integration-test-signing-key-32-bytes-minimum";

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly IReadOnlyList<IInterceptor> _interceptors;

    public CustomFormServiceApiFactory(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? overrides = null,
        IReadOnlyList<IInterceptor>? interceptors = null)
    {
        _environment = environment;
        _overrides = overrides ?? new Dictionary<string, string?>();
        _interceptors = interceptors ?? [];
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            if (!string.Equals(_environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
            {
                configuration.AddUserSecrets<Program>(optional: true);
                configuration.AddEnvironmentVariables();
            }

            var configuredValues = configuration.Build();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostgreSql:Host"] = configuredValues["PostgreSql:Host"] ?? "localhost",
                ["PostgreSql:Port"] = configuredValues["PostgreSql:Port"] ?? "5432",
                ["PostgreSql:Database"] = configuredValues["PostgreSql:Database"] ?? "custom_form_service",
                ["PostgreSql:Username"] = configuredValues["PostgreSql:Username"] ?? "custom_form_service",
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SigningKey"] = JwtSigningKey
            });
            configuration.AddInMemoryCollection(_overrides);
        });
        builder.ConfigureServices(services =>
        {
            foreach (var interceptor in _interceptors)
            {
                services.AddSingleton(typeof(IInterceptor), interceptor);
            }
        });
    }
}
