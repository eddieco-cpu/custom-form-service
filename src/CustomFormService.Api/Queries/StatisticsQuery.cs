using System.Text.Json;
using CustomFormService.Api.Contracts.Forms;
using CustomFormService.Api.Contracts.Statistics;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Queries;

public sealed class StatisticsQuery(ApplicationDbContext database)
{
    public Task<StatisticsForm?> FindFormAsync(Guid id, CancellationToken cancellationToken) =>
        database.Forms.AsNoTracking().Where(form => form.Id == id && form.DeletedAt == null)
            .Select(form => new StatisticsForm(form.Id, form.UserId, form.Status, form.Title, form.Description,
                form.ResponseMode, form.ResponseIdentity, form.ResultVisibility, form.SubmissionDeadlineAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<StatisticsResponse> ReadAsync(StatisticsForm form, CancellationToken cancellationToken)
    {
        // Select whole responses before joining Answers, so an empty latest response survives.
        var effective = await database.Database.SqlQuery<EffectiveStatisticsResponse>($"""
            SELECT DISTINCT ON (user_id) id AS "Id", user_id AS "UserId"
            FROM responses
            WHERE form_id = {form.Id}
            ORDER BY user_id, submitted_at DESC, id DESC
            """).ToArrayAsync(cancellationToken);
        var responseIds = effective.Select(response => response.Id).ToArray();
        var usersByResponse = effective.ToDictionary(response => response.Id, response => response.UserId);
        var questions = await database.Questions.AsNoTracking().Where(question => question.FormId == form.Id)
            .OrderBy(question => question.SortOrder)
            .Select(question => new StatisticsQuestion(question.Id, question.Title, question.Description,
                question.Type, question.Config)).ToArrayAsync(cancellationToken);
        var options = await database.Options.AsNoTracking().Where(option => option.Question.FormId == form.Id)
            .OrderBy(option => option.SortOrder)
            .Select(option => new StatisticsOption(option.Id, option.QuestionId, option.Label, option.Value))
            .ToArrayAsync(cancellationToken);
        var answers = await database.Answers.AsNoTracking().Where(answer => responseIds.Contains(answer.ResponseId))
            .Select(answer => new { answer.ResponseId, answer.QuestionId, answer.Value }).ToArrayAsync(cancellationToken);
        var creatorName = await database.Users.AsNoTracking().Where(user => user.Id == form.UserId)
            .Select(user => user.DisplayName).SingleAsync(cancellationToken);
        Dictionary<Guid, StatisticsRespondent>? respondents = null;
        if (form.ResponseIdentity == ResponseIdentity.Identified)
        {
            var userIds = effective.Select(response => response.UserId).ToArray();
            respondents = await database.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
                .Select(user => new StatisticsRespondent(user.Id, user.DisplayName))
                .ToDictionaryAsync(user => user.Id, cancellationToken);
        }
        var effectiveAnswers = answers.Select(answer => new StatisticsAnswer(
            answer.QuestionId, usersByResponse[answer.ResponseId], answer.Value.Clone())).ToArray();
        var questionIds = questions.Select(question => question.Id).ToHashSet();
        if (effectiveAnswers.Any(answer => !questionIds.Contains(answer.QuestionId)))
            throw new InvalidOperationException("Invalid persisted answer scope.");
        return new(form.Id, form.Title, form.Description, creatorName,
            FormDetailResponse.ToApiValue(form.ResponseMode), FormDetailResponse.ToApiValue(form.ResultVisibility),
            form.SubmissionDeadlineAt?.ToUniversalTime(), effective.Length,
            StatisticsAggregator.Aggregate(questions, options, effectiveAnswers, respondents));
    }
}

public sealed record StatisticsForm(Guid Id, Guid UserId, FormStatus Status, string Title, string? Description,
    ResponseMode ResponseMode, ResponseIdentity ResponseIdentity, ResultVisibility ResultVisibility,
    DateTimeOffset? SubmissionDeadlineAt);
internal sealed class EffectiveStatisticsResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
}
internal sealed record StatisticsQuestion(Guid Id, string Title, string? Description, QuestionType Type, JsonElement Config);
internal sealed record StatisticsOption(Guid Id, Guid QuestionId, string Label, string Value);
internal sealed record StatisticsAnswer(Guid QuestionId, Guid UserId, JsonElement Value);
