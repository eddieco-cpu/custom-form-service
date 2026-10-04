using System.Text.Json;

namespace CustomFormService.Api.Contracts.Responses;

public sealed record ResponseReceipt(
    Guid Id,
    Guid FormId,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<ResponseAnswerReceipt> Answers);

public sealed record ResponseAnswerReceipt(Guid QuestionId, JsonElement Value);
