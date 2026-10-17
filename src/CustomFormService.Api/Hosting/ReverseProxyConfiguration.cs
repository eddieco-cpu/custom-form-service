using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace CustomFormService.Api.Hosting;

public static class ReverseProxyConfiguration
{
    public static IServiceCollection AddReverseProxyConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The framework's automatic switch clears proxy restrictions. Do not allow
        // it to install an unrestricted middleware before our explicit settings.
        if (configuration.GetValue<bool>("ForwardedHeaders_Enabled"))
        {
            throw new InvalidOperationException(
                "Automatic forwarded headers are not supported; configure ReverseProxy:KnownProxies.");
        }

        services.AddOptions<ReverseProxyOptions>()
            .BindConfiguration(ReverseProxyOptions.SectionName)
            .Validate(options => options.KnownProxies.All(value =>
                IPAddress.TryParse(value, out var address) &&
                !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)),
                "Known proxies must be explicit IP addresses.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((options, configuredOptions) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                // Retain framework loopback restrictions even with an empty list.
                foreach (var value in configuredOptions.Value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(value));
                }
            });
        services.AddHttpsRedirection(options => options.HttpsPort = 443);
        return services;
    }

    public static bool IsHealthRequest(HttpContext context) =>
        context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);
}
