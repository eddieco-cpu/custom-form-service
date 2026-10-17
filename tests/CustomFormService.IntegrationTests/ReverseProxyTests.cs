using System.Net;
using CustomFormService.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class ReverseProxyTests
{
    [TestMethod]
    [DataRow("192.0.2.10", "https")]
    [DataRow("192.0.2.11", "http")]
    public async Task Only_an_explicit_proxy_can_change_scheme(string peer, string expectedScheme)
    {
        using var services = CreateServices(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "192.0.2.10"
        });
        var options = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("original.test");
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "spoofed.test";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.5";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            NullLoggerFactory.Instance, Options.Create(options));

        await middleware.Invoke(context);

        Assert.AreEqual(expectedScheme, context.Request.Scheme);
        Assert.AreEqual("original.test", context.Request.Host.Host);
        Assert.AreEqual(IPAddress.Parse(peer), context.Connection.RemoteIpAddress);
        Assert.AreEqual(1, options.ForwardLimit);
    }

    [TestMethod]
    [DataRow("not-an-ip")]
    [DataRow("0.0.0.0")]
    [DataRow("::")]
    [DataRow("192.0.2.0/24")]
    public void Invalid_or_unspecified_proxy_is_rejected(string proxy)
    {
        using var services = CreateServices(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = proxy
        });
        Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }

    [TestMethod]
    public void Unrestricted_framework_forwarding_switch_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => CreateServices(new Dictionary<string, string?>
        {
            ["ForwardedHeaders_Enabled"] = "true"
        }));
    }

    [TestMethod]
    public async Task Production_http_health_bypasses_redirect_but_other_paths_redirect()
    {
        using var factory = CreateProductionFactory(redirect: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var health = await client.GetAsync("/health");
        var protectedResponse = await client.GetAsync("/api/v1/users/me");

        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, protectedResponse.StatusCode);
        Assert.AreEqual("https", protectedResponse.Headers.Location?.Scheme);
    }

    [TestMethod]
    public async Task Edge_tls_mode_accepts_internal_http_without_opening_Cors()
    {
        using var factory = CreateProductionFactory(redirect: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/api/v1/users/me");
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", "https://unconfigured.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var cors = await client.SendAsync(preflight);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsFalse(cors.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.IsFalse(cors.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private static ServiceProvider CreateServices(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddReverseProxyConfiguration(configuration);
        return services.BuildServiceProvider();
    }

    private static CustomFormServiceApiFactory CreateProductionFactory(bool redirect) => new(
        "Production", new Dictionary<string, string?>
        {
            ["PostgreSql:Password"] = "proxy-test-synthetic-password",
            ["ReverseProxy:HttpsRedirectionEnabled"] = redirect.ToString(),
            ["Cors:AllowedOrigins"] = null
        });
}
