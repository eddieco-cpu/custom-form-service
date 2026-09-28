using System.Text.Json;

namespace CustomFormService.Api.Domain.Entities;

public sealed class Answer
{
    public Guid Id { get; set; }

    public Guid ResponseId { get; set; }

    public Guid QuestionId { get; set; }

    public JsonElement Value { get; set; }

    public Response Response { get; set; } = null!;

    public Question Question { get; set; } = null!;
}
