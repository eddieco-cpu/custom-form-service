using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomFormService.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    private const string FailureReasonKey = "authentication_failure_reason";
    private const string ExpiredReason = "expired";
    private const string DisabledReason = "disabled";

    public static IServiceCollection AddV1Authentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "JWT issuer must be configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "JWT audience must be configured.")
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32, "JWT signing key must be at least 32 bytes.")
            .Validate(
                options => !string.Equals(options.SigningKey, "replace-with-at-least-32-random-characters", StringComparison.Ordinal),
                "The example JWT signing key must be replaced before the API starts.")
            .Validate(options => options.LifetimeMinutes > 0, "JWT lifetime must be positive.")
            .ValidateOnStart();

        services
            .AddOptions<AccessTokenCookieOptions>()
            .Bind(configuration.GetSection(AccessTokenCookieOptions.SectionName))
            .Validate(options => options.Secure.HasValue, "Access-token cookie Secure must be configured.")
            .Validate(
                options => environment.IsDevelopment() || options.Secure is true,
                "Access-token cookies must be Secure outside Development.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddAuthorization();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, configuredOptions) =>
            {
                var jwt = configuredOptions.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
                };
                options.Events = CreateEvents();
            });

        return services;
    }

    private static JwtBearerEvents CreateEvents() => new()
    {
        OnMessageReceived = context =>
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                context.NoResult();
                return Task.CompletedTask;
            }

            if (context.Request.Cookies.TryGetValue(AccessTokenCookie.Name, out var cookieToken))
            {
                context.Token = string.IsNullOrEmpty(cookieToken) ? "invalid-empty-token" : cookieToken;
            }
            else
            {
                context.NoResult();
            }

            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.HttpContext.Items[FailureReasonKey] = ExpiredReason;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(subject, out var userId))
            {
                context.Fail("The token subject is invalid.");
                return;
            }

            var database = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == userId,
                context.HttpContext.RequestAborted);
            if (user is null)
            {
                context.Fail("The token user does not exist.");
                return;
            }

            if (user.Status != UserStatus.Active)
            {
                context.HttpContext.Items[FailureReasonKey] = DisabledReason;
                context.Fail("The token user is disabled.");
                return;
            }

            context.HttpContext.RequestServices.GetRequiredService<CurrentUserContext>().User = user;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            if (context.Response.HasStarted)
            {
                return;
            }

            var reason = context.HttpContext.Items[FailureReasonKey] as string;
            var (status, code, message) = reason switch
            {
                DisabledReason => (StatusCodes.Status403Forbidden, "AUTH_USER_DISABLED", "This user account is disabled."),
                ExpiredReason => (StatusCodes.Status401Unauthorized, "AUTH_TOKEN_EXPIRED", "Your session has expired. Please sign in again."),
                _ when !context.Request.Cookies.ContainsKey(AccessTokenCookie.Name) =>
                    (StatusCodes.Status401Unauthorized, "AUTH_REQUIRED", "Authentication is required."),
                _ => (StatusCodes.Status401Unauthorized, "AUTH_TOKEN_INVALID", "The authentication token is invalid.")
            };

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(ApiErrors.Payload(code, message));
        }
    };
}
