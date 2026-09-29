using Microsoft.Extensions.Options;

namespace CustomFormService.Api.Authentication;

public static class V1Cors
{
    public const string PolicyName = "V1Client";

    public static IServiceCollection AddV1Cors(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AppCorsOptions>()
            .Bind(configuration.GetSection(AppCorsOptions.SectionName))
            .Validate(
                options => options.AllowedOrigins.All(AppCorsOptions.IsCanonicalOrigin),
                "CORS origins must use canonical HTTP or HTTPS origin form without a trailing slash.")
            .ValidateOnStart();

        services.AddCors();
        services
            .AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<AppCorsOptions>>((options, configuredOptions) =>
            {
                options.AddPolicy(PolicyName, policy =>
                {
                    var origins = configuredOptions.Value.AllowedOrigins;
                    if (origins.Length > 0)
                    {
                        policy.WithOrigins(origins);
                    }

                    policy.AllowCredentials().AllowAnyHeader().AllowAnyMethod();
                });
            });

        return services;
    }
}
