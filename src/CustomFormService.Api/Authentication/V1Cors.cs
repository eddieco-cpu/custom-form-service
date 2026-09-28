namespace CustomFormService.Api.Authentication;

public static class V1Cors
{
    public const string PolicyName = "V1Client";

    public static IServiceCollection AddV1Cors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => origin == "*" || !Uri.TryCreate(origin, UriKind.Absolute, out _)))
        {
            throw new InvalidOperationException("CORS origins must be explicit absolute origins and cannot use a wildcard.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
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
