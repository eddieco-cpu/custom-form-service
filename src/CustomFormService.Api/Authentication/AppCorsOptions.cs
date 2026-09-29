namespace CustomFormService.Api.Authentication;

public sealed class AppCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public static bool IsCanonicalOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || origin.Contains('*', StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var canonicalOrigin = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        return string.Equals(origin, canonicalOrigin, StringComparison.Ordinal);
    }
}
