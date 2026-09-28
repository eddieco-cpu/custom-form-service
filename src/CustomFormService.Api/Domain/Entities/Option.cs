namespace CustomFormService.Api.Domain.Entities;

public sealed class Option
{
    public Guid Id { get; set; }

    public Guid QuestionId { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Question Question { get; set; } = null!;
}
