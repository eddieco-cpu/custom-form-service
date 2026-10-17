namespace CustomFormService.Api.Hosting;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    // Railway's edge enforces public HTTPS. Set false there, keeping private HTTP.
    public bool HttpsRedirectionEnabled { get; init; } = true;

    // Only exact, operator-verified proxy IP addresses. No implicit cloud ranges.
    public string[] KnownProxies { get; init; } = [];
}
