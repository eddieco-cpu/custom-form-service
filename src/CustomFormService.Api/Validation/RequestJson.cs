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
            if (!allowedProperties.Contains(property.Name))
            {
                validationErrors.Add(Invalid(property.Name, "This field is not allowed."));
                continue;
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                validationErrors.Add(Invalid(property.Name, "This field must not be repeated."));
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
        if (properties.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            error = null;
            return true;
        }

        value = string.Empty;
        error = Invalid(name, "This field must be a non-null string.");
        return false;
    }

    public static ApiErrorDetail Invalid(string path, string message) =>
        new(path, "INVALID_VALUE", message);
}
