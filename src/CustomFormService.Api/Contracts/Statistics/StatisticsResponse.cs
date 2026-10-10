using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomFormService.Api.Contracts.Statistics;

public sealed record StatisticsResponse(
    Guid FormId, string Title, string? Description, string CreatorDisplayName,
    string ResponseMode, string ResultVisibility, DateTimeOffset? SubmissionDeadlineAt,
    int Count, IReadOnlyList<StatisticsQuestionResponse> Questions);

public sealed record StatisticsQuestionResponse(
    Guid QuestionId, string Title, string? Description, string Type,
    int Count, IReadOnlyList<object> Statistics);

public sealed record StatisticsRespondent(Guid Id, string DisplayName);

public sealed record ChoiceStatisticsItem(
    Guid OptionId, string Label, string Value, int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<StatisticsRespondent>? Respondents);

// JsonElement keeps a persisted Number exact; it is never converted through decimal/double.
public sealed record ValueStatisticsItem(
    JsonElement Value, int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<StatisticsRespondent>? Respondents);
