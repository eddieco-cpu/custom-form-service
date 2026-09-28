using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed record HealthPayload(string Status);
}

public sealed class CustomFormServiceApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
    }
}
