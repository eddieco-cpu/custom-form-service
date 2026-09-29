namespace CustomFormService.Api.Authentication;

public sealed class AccessTokenCookieOptions
{
    public const string SectionName = "AccessTokenCookie";

    public bool? Secure { get; set; }
}
