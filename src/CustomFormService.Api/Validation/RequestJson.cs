using System.Text.Json;
using CustomFormService.Api.Contracts.Errors;

namespace CustomFormService.Api.Validation;

public static class RequestJson
{
    public static bool TryReadObject(
        JsonElement body,
        IReadOnlySet<string> allowedProperties,
        IReadOnlySet<string> requiredProperties,
        out Dictionary<string, JsonElement> properties,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var validationErrors = new List<ApiErrorDetail>();

        if (body.ValueKind != JsonValueKind.Object)
        {
            errors = [Invalid("body", "The request body must be a JSON object.")];
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            if (!TryReadPropertyName(property, out var name))
            {
                validationErrors.Add(Invalid("body", "The object contains an invalid JSON property name."));
                continue;
            }
            if (!allowedProperties.Contains(name))
            {
                validationErrors.Add(Invalid(name, "This field is not allowed."));
                continue;
            }

            if (!properties.TryAdd(name, property.Value))
            {
                validationErrors.Add(Invalid(name, "This field must not be repeated."));
            }
        }

        foreach (var required in requiredProperties)
        {
            if (!properties.ContainsKey(required))
            {
                validationErrors.Add(new ApiErrorDetail(required, "REQUIRED_FIELD", "This field is required."));
            }
        }

        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    public static bool TryGetString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out string value,
        out ApiErrorDetail? error)
    {
        if (properties.TryGetValue(name, out var element) && TryReadString(element, out value))
        {
            error = null;
            return true;
        }

        value = string.Empty;
        error = Invalid(name, "This field must be a non-null string.");
        return false;
    }

    public static ApiErrorDetail Invalid(string path, string message) =>
        new(path, "INVALID_VALUE", message);

    public static bool TryReadString(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String) return false;
        try { value = element.GetString()!; return true; }
        catch (InvalidOperationException) { return false; } // Only the JSON string decode boundary.
    }

    public static bool TryReadPropertyName(JsonProperty property, out string name)
    {
        name = string.Empty;
        try { name = property.Name; return true; }
        catch (InvalidOperationException) { return false; } // Escaped lone surrogate in a name.
    }
}
