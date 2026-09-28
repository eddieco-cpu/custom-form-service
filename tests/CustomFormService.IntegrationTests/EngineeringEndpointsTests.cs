using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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

    private sealed record HealthPayload(string Status);
}

public sealed class CustomFormServiceApiFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    public const string JwtIssuer = "custom-form-service-tests";
    public const string JwtAudience = "custom-form-service-test-client";
    public const string JwtSigningKey = "R4-integration-test-signing-key-32-bytes-minimum";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Jwt:LifetimeMinutes"] = "60"
            });
        });
    }
}
