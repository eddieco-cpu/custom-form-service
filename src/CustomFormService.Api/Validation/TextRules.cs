using System.Net.Mail;

namespace CustomFormService.Api.Validation;

public static class TextRules
{
    public static string Trim(string value) => value.Trim();

    public static string NormalizeEmail(string value) => Trim(value).ToLowerInvariant();

    public static int CodePointCount(string value) => value.EnumerateRunes().Count();

    public static bool IsValidEmail(string value)
    {
        return MailAddress.TryCreate(value, out var address)
            && string.Equals(address.Address, value, StringComparison.Ordinal)
            && value.Contains('@', StringComparison.Ordinal);
    }
}
