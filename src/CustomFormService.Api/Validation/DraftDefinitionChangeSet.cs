using System.Text.Json;
using CustomFormService.Api.Domain.Entities;

namespace CustomFormService.Api.Validation;

public sealed record DraftDefinitionChangeSet(
    FormDefinitionCandidate Candidate,
    bool FormFieldsChanged,
    IReadOnlyList<Guid> DeletedQuestions,
    IReadOnlyList<QuestionDefinitionChange> Questions)
{
    // Npgsql 10.0.3 PgTimestamp encodes whole microseconds relative to 2000-01-01,
    // truncating toward zero (not flooring .NET ticks). Keep its default infinity
    // conversions for DateTime.MinValue/MaxValue distinct from finite timestamps.
    // https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs
    private static readonly long PostgreSqlEpochTicks = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public bool HasChanges => FormFieldsChanged || DeletedQuestions.Count > 0 || Questions.Count > 0;

    public static DraftDefinitionChangeSet Between(Form form, FormDefinitionCandidate candidate)
    {
        var existing = form.Questions.ToDictionary(question => question.Id);
        var finalIds = candidate.Questions.Select(question => question.Id).ToHashSet();
        var changes = new List<QuestionDefinitionChange>();
        foreach (var question in candidate.Questions)
        {
            var isNew = !existing.TryGetValue(question.Id, out var before);
            var scalarChange = isNew || before!.Type != question.Type || before.Title != question.Title ||
                before.Description != question.Description || before.Required != question.Required ||
                before.SortOrder != question.SortOrder || !JsonElement.DeepEquals(before.Config, question.Config);
            var oldOptions = before?.Options.ToDictionary(option => option.Id) ?? [];
            var finalOptionIds = question.Options.Select(option => option.Id).ToHashSet();
            var deletedOptions = oldOptions.Keys.Where(id => !finalOptionIds.Contains(id)).ToArray();
            var optionChanges = new List<OptionDefinitionChange>();
            foreach (var option in question.Options)
            {
                var added = !oldOptions.TryGetValue(option.Id, out var old);
                if (added || old!.Label != option.Label || old.Value != option.Value || old.SortOrder != option.SortOrder)
                {
                    optionChanges.Add(new(option, added, !added && old!.Value != option.Value));
                }
            }
            if (scalarChange || deletedOptions.Length > 0 || optionChanges.Count > 0)
            {
                changes.Add(new(question, isNew, scalarChange, deletedOptions, optionChanges));
            }
        }
        return new(candidate,
            form.Title != candidate.Title || form.Description != candidate.Description ||
            form.ResponseMode != candidate.ResponseMode || form.ResponseIdentity != candidate.ResponseIdentity ||
            !DeadlinesEqualAtStoragePrecision(form.SubmissionDeadlineAt, candidate.SubmissionDeadlineAt),
            existing.Keys.Where(id => !finalIds.Contains(id)).ToArray(), changes);
    }

    private static bool DeadlinesEqualAtStoragePrecision(DateTimeOffset? before, DateTimeOffset? after)
    {
        if (before == after) return true;
        if (before is not { } saved || after is not { } requested) return false;
        return EncodeDeadline(saved) == EncodeDeadline(requested);
    }

    private static long EncodeDeadline(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        if (ticks == DateTime.MinValue.Ticks) return long.MinValue;
        if (ticks == DateTime.MaxValue.Ticks) return long.MaxValue;
        return (ticks - PostgreSqlEpochTicks) / TimeSpan.TicksPerMicrosecond;
    }
}

public sealed record QuestionDefinitionChange(
    QuestionDefinitionCandidate Candidate,
    bool IsNew,
    bool FieldsChanged,
    IReadOnlyList<Guid> DeletedOptions,
    IReadOnlyList<OptionDefinitionChange> Options);

public sealed record OptionDefinitionChange(OptionDefinitionCandidate Candidate, bool IsNew, bool ReplaceValue);
