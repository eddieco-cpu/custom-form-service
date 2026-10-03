using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Validation;

// Definition data only: no EF entities, navigations, timestamps or tracker state.
public sealed class FormDefinitionCandidate
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ResponseMode ResponseMode { get; set; }
    public ResponseIdentity ResponseIdentity { get; set; }
    public DateTimeOffset? SubmissionDeadlineAt { get; set; }
    public List<QuestionDefinitionCandidate> Questions { get; } = [];

    public static FormDefinitionCandidate From(Form form)
    {
        var candidate = new FormDefinitionCandidate
        {
            Title = form.Title, Description = form.Description, ResponseMode = form.ResponseMode,
            ResponseIdentity = form.ResponseIdentity, SubmissionDeadlineAt = form.SubmissionDeadlineAt
        };
        foreach (var question in form.Questions)
        {
            var item = new QuestionDefinitionCandidate
            {
                Id = question.Id, Type = question.Type, Title = question.Title, Description = question.Description,
                Required = question.Required, SortOrder = question.SortOrder, Config = question.Config.Clone()
            };
            item.Options.AddRange(question.Options.Select(option => new OptionDefinitionCandidate
            {
                Id = option.Id, Label = option.Label, Value = option.Value, SortOrder = option.SortOrder
            }));
            candidate.Questions.Add(item);
        }
        return candidate;
    }
}

public sealed class QuestionDefinitionCandidate
{
    public Guid Id { get; init; }
    public QuestionType Type { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool Required { get; set; }
    public int SortOrder { get; set; }
    public JsonElement Config { get; set; }
    public List<OptionDefinitionCandidate> Options { get; } = [];
    public DefinitionRequestSource Source { get; } = new();
}

public sealed class OptionDefinitionCandidate
{
    public Guid Id { get; init; }
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public int SortOrder { get; set; }
    public DefinitionRequestSource Source { get; } = new();
}

// Sources are attached to individual supplied fields, not to "touched" items.
// Changed distinguishes a causal sort/value input from an explicit unchanged input.
public sealed class DefinitionRequestSource
{
    private readonly Dictionary<string, (string Path, bool Changed)> _fields = new(StringComparer.Ordinal);

    public void Set(string field, string path, bool changed = true) => _fields[field] = (path, changed);

    public string Path(string field) => _fields.TryGetValue(field, out var source) ? source.Path : "questions";

    public string ConflictPath(string field, DefinitionRequestSource other)
    {
        var supplied = _fields.TryGetValue(field, out var source);
        var otherSupplied = other._fields.TryGetValue(field, out var otherSource);
        if (supplied && source.Changed) return source.Path;
        if (otherSupplied && otherSource.Changed) return otherSource.Path;
        if (supplied) return source.Path;
        return otherSupplied ? otherSource.Path : "questions";
    }
}
