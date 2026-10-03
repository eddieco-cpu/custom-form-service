using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Validation;

namespace CustomFormService.Api.Infrastructure.Persistence;

// The caller owns the Form lock and transaction. No incoming Option entity exists
// in the tracked graph until every outgoing alternate key has been saved away.
public static class DraftDefinitionWriter
{
    public static async Task SaveAsync(
        ApplicationDbContext database,
        Form form,
        DraftDefinitionChangeSet changes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!changes.HasChanges) return;
        if (changes.FormFieldsChanged)
        {
            form.Title = changes.Candidate.Title;
            form.Description = changes.Candidate.Description;
            form.ResponseMode = changes.Candidate.ResponseMode;
            form.ResponseIdentity = changes.Candidate.ResponseIdentity;
            form.SubmissionDeadlineAt = changes.Candidate.SubmissionDeadlineAt;
        }
        form.UpdatedAt = now;
        var questions = form.Questions.ToDictionary(question => question.Id);
        foreach (var id in changes.DeletedQuestions)
        {
            var question = questions[id];
            database.Questions.Remove(question);
            form.Questions.Remove(question);
        }
        var pending = new List<PendingOption>();
        foreach (var change in changes.Questions)
        {
            var definition = change.Candidate;
            Question question;
            if (change.IsNew)
            {
                question = new()
                {
                    Id = definition.Id, FormId = form.Id, Form = form, CreatedAt = now,
                    Type = definition.Type, Title = definition.Title, Description = definition.Description,
                    Required = definition.Required, SortOrder = definition.SortOrder, Config = definition.Config
                };
                form.Questions.Add(question);
                database.Questions.Add(question);
            }
            else question = questions[definition.Id];
            if (change.FieldsChanged)
            {
                question.Type = definition.Type;
                question.Title = definition.Title;
                question.Description = definition.Description;
                question.Required = definition.Required;
                question.SortOrder = definition.SortOrder;
                if (change.IsNew || !JsonElement.DeepEquals(question.Config, definition.Config))
                    question.Config = definition.Config;
            }
            question.UpdatedAt = now;
            var options = question.Options.ToDictionary(option => option.Id);
            foreach (var id in change.DeletedOptions)
            {
                database.Options.Remove(options[id]);
                question.Options.Remove(options[id]);
            }
            foreach (var optionChange in change.Options)
            {
                if (optionChange.IsNew)
                {
                    pending.Add(new(optionChange.Candidate, question, now));
                    continue;
                }
                var option = options[optionChange.Candidate.Id];
                if (optionChange.ReplaceValue)
                {
                    database.Options.Remove(option);
                    question.Options.Remove(option);
                    pending.Add(new(optionChange.Candidate, question, option.CreatedAt));
                }
                else
                {
                    option.Label = optionChange.Candidate.Label;
                    option.SortOrder = optionChange.Candidate.SortOrder;
                    option.UpdatedAt = now;
                }
            }
        }
        // Normal change detection is safe: pending entries are definition data,
        // not detached entities reachable from a tracked navigation.
        await database.SaveChangesAsync(cancellationToken);
        if (pending.Count == 0) return;
        foreach (var write in pending)
        {
            var option = new Option
            {
                Id = write.Definition.Id, QuestionId = write.Question.Id, Question = write.Question,
                Label = write.Definition.Label, Value = write.Definition.Value, SortOrder = write.Definition.SortOrder,
                CreatedAt = write.CreatedAt, UpdatedAt = now
            };
            write.Question.Options.Add(option);
            database.Options.Add(option);
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    private sealed record PendingOption(OptionDefinitionCandidate Definition, Question Question, DateTimeOffset CreatedAt);
}
