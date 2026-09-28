using System.Text.Json;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Domain.Entities;

public sealed class Question
{
    public Guid Id { get; set; }

    public Guid FormId { get; set; }

    public QuestionType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Required { get; set; }

    public int SortOrder { get; set; }

    public JsonElement Config { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Form Form { get; set; } = null!;

    public ICollection<Option> Options { get; } = [];

    public ICollection<Answer> Answers { get; } = [];
}
