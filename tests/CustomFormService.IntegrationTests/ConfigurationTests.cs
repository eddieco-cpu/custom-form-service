using System.IdentityModel.Tokens.Jwt;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class ConfigurationTests
{
    private const string TestPostgreSqlPassword = "configuration-test-password";

    [TestMethod]
    public void Jwt_default_lifetime_is_1440_minutes()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        var issued = scope.ServiceProvider.GetRequiredService<JwtTokenService>().Create(new User { Id = Guid.NewGuid() });
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);

        Assert.AreEqual(TimeSpan.FromMinutes(1440), token.ValidTo - token.ValidFrom);
    }

    [TestMethod]
    public void Positive_Jwt_lifetime_override_is_accepted()
    {
        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["Jwt:LifetimeMinutes"] = "60"
        });
        using var scope = factory.Services.CreateScope();

        var issued = scope.ServiceProvider.GetRequiredService<JwtTokenService>().Create(new User { Id = Guid.NewGuid() });
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);

        Assert.AreEqual(TimeSpan.FromMinutes(60), token.ValidTo - token.ValidFrom);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    public void Nonpositive_Jwt_lifetime_fails_startup(string lifetime)
    {
        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["Jwt:LifetimeMinutes"] = lifetime
        });

        AssertStartupFails(factory, "JWT lifetime must be positive");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("short")]
    [DataRow("replace-with-at-least-32-random-characters")]
    public void Missing_invalid_or_placeholder_Jwt_key_fails_startup(string signingKey)
    {
        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = signingKey
        });

        AssertStartupFails(factory, "JWT signing key");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("replace-with-a-local-password")]
    public void Missing_or_placeholder_PostgreSql_password_fails_startup(string password)
    {
        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["PostgreSql:Password"] = password
        });

        AssertStartupFails(factory, "PostgreSQL password");
    }

    [TestMethod]
    [DataRow("Staging")]
    [DataRow("Production")]
    public void Insecure_access_token_cookie_fails_outside_Development(string environment)
    {
        using var factory = CreateFactory(environment, new Dictionary<string, string?>
        {
            ["AccessTokenCookie:Secure"] = "false"
        });

        AssertStartupFails(factory, "Access-token cookies must be Secure outside Development");
    }

    [TestMethod]
    public void Empty_Cors_origin_list_is_allowed()
    {
        using var factory = CreateFactory("Production", new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins"] = null
        });
        using var client = factory.CreateClient();

        Assert.IsNotNull(client);
    }

    [TestMethod]
    [DataRow("http://localhost:3000")]
    [DataRow("https://app.example.com")]
    [DataRow("https://app.example.com:8443")]
    public void Canonical_HTTP_origins_are_valid(string origin)
    {
        Assert.IsTrue(AppCorsOptions.IsCanonicalOrigin(origin));
    }

    [TestMethod]
    [DataRow("*")]
    [DataRow("http://localhost:3000/")]
    [DataRow("https://app.example.com/path")]
    [DataRow("https://app.example.com?query=value")]
    [DataRow("https://app.example.com#fragment")]
    [DataRow("https://user@app.example.com")]
    [DataRow("ftp://app.example.com")]
    [DataRow("HTTPS://APP.EXAMPLE.COM")]
    [DataRow("https://app.example.com:443")]
    public void Noncanonical_Cors_origins_are_invalid(string origin)
    {
        Assert.IsFalse(AppCorsOptions.IsCanonicalOrigin(origin));
    }

    [TestMethod]
    public void Noncanonical_Cors_origin_fails_startup()
    {
        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:3000/"
        });

        AssertStartupFails(factory, "CORS origins must use canonical HTTP or HTTPS origin form");
    }

    private static CustomFormServiceApiFactory CreateFactory(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["PostgreSql:Password"] = TestPostgreSqlPassword
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                configuration[key] = value;
            }
        }

        return new CustomFormServiceApiFactory(environment, configuration);
    }

    private static void AssertStartupFails(CustomFormServiceApiFactory factory, string expectedMessage)
    {
        try
        {
            using var client = factory.CreateClient();
            Assert.Fail("The application was expected to fail during startup validation.");
        }
        catch (Exception exception)
        {
            StringAssert.Contains(exception.ToString(), expectedMessage);
        }
    }
}
