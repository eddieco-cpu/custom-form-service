using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Contracts.Forms;

public sealed record FormDetailResponse(
    Guid Id,
    Guid UserId,
    string CreatorDisplayName,
    string Title,
    string? Description,
    string Status,
    string ResponseMode,
    string ResponseIdentity,
    string ResultVisibility,
    DateTimeOffset? SubmissionDeadlineAt,
    IReadOnlyList<QuestionResponse> Questions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FormDetailResponse From(Form form) =>
        new(
            form.Id,
            form.UserId,
            form.User.DisplayName,
            form.Title,
            form.Description,
            ToApiValue(form.Status),
            ToApiValue(form.ResponseMode),
            ToApiValue(form.ResponseIdentity),
            ToApiValue(form.ResultVisibility),
            form.SubmissionDeadlineAt,
            form.Questions
                .OrderBy(question => question.SortOrder)
                .ThenBy(question => question.Id)
                .Select(QuestionResponse.From)
                .ToArray(),
            form.CreatedAt,
            form.UpdatedAt);

    public static string ToApiValue(FormStatus value) => value switch
    {
        FormStatus.Draft => "draft",
        FormStatus.Published => "published",
        FormStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToApiValue(CustomFormService.Api.Domain.Enums.ResponseMode value) => value switch
    {
        CustomFormService.Api.Domain.Enums.ResponseMode.Single => "single",
        CustomFormService.Api.Domain.Enums.ResponseMode.Multiple => "multiple",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToApiValue(CustomFormService.Api.Domain.Enums.ResponseIdentity value) => value switch
    {
        CustomFormService.Api.Domain.Enums.ResponseIdentity.Anonymous => "anonymous",
        CustomFormService.Api.Domain.Enums.ResponseIdentity.Identified => "identified",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToApiValue(CustomFormService.Api.Domain.Enums.ResultVisibility value) => value switch
    {
        CustomFormService.Api.Domain.Enums.ResultVisibility.OwnerOnly => "owner_only",
        CustomFormService.Api.Domain.Enums.ResultVisibility.Respondents => "respondents",
        CustomFormService.Api.Domain.Enums.ResultVisibility.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

public sealed record QuestionResponse(
    Guid Id,
    string Type,
    string Title,
    string? Description,
    bool Required,
    int SortOrder,
    JsonElement Config,
    IReadOnlyList<OptionResponse> Options)
{
    public static QuestionResponse From(Question question) =>
        new(
            question.Id,
            question.Type switch
            {
                QuestionType.Text => "text",
                QuestionType.Number => "number",
                QuestionType.Rating => "rating",
                QuestionType.SingleChoice => "single_choice",
                QuestionType.MultipleChoice => "multiple_choice",
                _ => throw new ArgumentOutOfRangeException(nameof(question.Type))
            },
            question.Title,
            question.Description,
            question.Required,
            question.SortOrder,
            question.Config,
            question.Options
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Id)
                .Select(OptionResponse.From)
                .ToArray());
}

public sealed record OptionResponse(Guid Id, string Label, string Value, int SortOrder)
{
    public static OptionResponse From(Option option) =>
        new(option.Id, option.Label, option.Value, option.SortOrder);
}

public sealed record FormSummaryResponse(
    Guid Id,
    string Title,
    string Status,
    string ResponseMode,
    string ResponseIdentity,
    string ResultVisibility,
    DateTimeOffset? SubmissionDeadlineAt,
    int Count,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FormListResponse(
    IReadOnlyList<FormSummaryResponse> Items,
    FormPaginationResponse Pagination);

public sealed record FormPaginationResponse(int Page, int PageSize, int TotalItems, int TotalPages);
