namespace CustomFormService.Api.Authentication;

public static class AccessTokenCookie
{
    public const string Name = "access_token";

    public static void Append(
        HttpResponse response,
        IssuedAccessToken token,
        AccessTokenCookieOptions configuredOptions)
    {
        var options = BaseOptions(configuredOptions);
        options.Expires = token.ExpiresAt;
        response.Cookies.Append(Name, token.Value, options);
    }

    public static void Delete(HttpResponse response, AccessTokenCookieOptions configuredOptions)
    {
        response.Cookies.Delete(Name, BaseOptions(configuredOptions));
    }

    private static CookieOptions BaseOptions(AccessTokenCookieOptions configuredOptions) => new()
    {
        HttpOnly = true,
        Secure = configuredOptions.Secure!.Value,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };
}
