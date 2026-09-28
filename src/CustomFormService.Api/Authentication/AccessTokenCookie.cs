namespace CustomFormService.Api.Authentication;

public static class AccessTokenCookie
{
    public const string Name = "access_token";

    public static void Append(HttpResponse response, string token, IHostEnvironment environment)
    {
        response.Cookies.Append(Name, token, Options(environment));
    }

    public static void Delete(HttpResponse response, IHostEnvironment environment)
    {
        response.Cookies.Delete(Name, Options(environment));
    }

    private static CookieOptions Options(IHostEnvironment environment) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };
}
