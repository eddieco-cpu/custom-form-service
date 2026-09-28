using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CustomFormService.Api.Contracts.Errors;

public static class ApiErrors
{
    public static ObjectResult Validation(IReadOnlyList<ApiErrorDetail> details) =>
        Result(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "The request is invalid.", details);

    public static ObjectResult InvalidCredentials() =>
        Result(StatusCodes.Status401Unauthorized, "AUTH_INVALID_CREDENTIALS", "The email or password is incorrect.");

    public static ObjectResult UserDisabled() =>
        Result(StatusCodes.Status403Forbidden, "AUTH_USER_DISABLED", "This user account is disabled.");

    public static ObjectResult EmailAlreadyExists() =>
        Result(StatusCodes.Status409Conflict, "USER_EMAIL_ALREADY_EXISTS", "A user with this email already exists.");

    public static ObjectResult InvalidModelState(ModelStateDictionary modelState)
    {
        var details = modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiErrorDetail(
                NormalizeModelPath(entry.Key),
                IsRequired(error.ErrorMessage) ? "REQUIRED_FIELD" : "INVALID_VALUE",
                "The request body is invalid.")))
            .ToArray();

        if (details.Length == 0)
        {
            details = [new ApiErrorDetail("body", "INVALID_VALUE", "The request body is invalid.")];
        }

        return Validation(details);
    }

    public static ApiErrorResponse Payload(
        string code,
        string message,
        IReadOnlyList<ApiErrorDetail>? details = null) =>
        new(new ApiError(code, message, details));

    public static ObjectResult Result(
        int statusCode,
        string code,
        string message,
        IReadOnlyList<ApiErrorDetail>? details = null) =>
        new(Payload(code, message, details)) { StatusCode = statusCode };

    private static string NormalizeModelPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            return "body";
        }

        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
    }

    private static bool IsRequired(string message) =>
        message.Contains("required", StringComparison.OrdinalIgnoreCase);
}
