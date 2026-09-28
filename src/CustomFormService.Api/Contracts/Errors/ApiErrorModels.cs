using System.Text.Json.Serialization;

namespace CustomFormService.Api.Contracts.Errors;

public sealed record ApiErrorResponse(ApiError Error);

public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApiErrorDetail>? Details = null);

public sealed record ApiErrorDetail(string Path, string Code, string Message);
