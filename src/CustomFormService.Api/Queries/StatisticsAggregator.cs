using System.Text.Json;
using CustomFormService.Api.Contracts.Statistics;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Validation;

namespace CustomFormService.Api.Queries;

internal static class StatisticsAggregator
{
    public static StatisticsQuestionResponse[] Aggregate(
        StatisticsQuestion[] questions, StatisticsOption[] options, StatisticsAnswer[] answers,
        IReadOnlyDictionary<Guid, StatisticsRespondent>? respondents)
    {
        var byQuestion = answers.ToLookup(answer => answer.QuestionId);
        var byOptionQuestion = options.ToLookup(option => option.QuestionId);
        return questions.Select(question =>
        {
            var values = byQuestion[question.Id].ToArray();
            object[] items = question.Type switch
            {
                QuestionType.Text => values.GroupBy(answer => answer.Value.GetString()
                        ?? throw new InvalidOperationException("Invalid persisted text."), StringComparer.Ordinal)
                    .OrderByDescending(group => group.Select(answer => answer.UserId).Distinct().Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => (object)Value(group.First().Value, group.Select(answer => answer.UserId), respondents)).ToArray(),
                QuestionType.Number => values.GroupBy(answer => ReadNumber(answer.Value))
                    .OrderBy(group => group.Key)
                    .Select(group => (object)Value(group.First().Value, group.Select(answer => answer.UserId), respondents)).ToArray(),
                QuestionType.SingleChoice or QuestionType.MultipleChoice => Choice(question, byOptionQuestion[question.Id], values, respondents),
                QuestionType.Rating => Rating(question, values, respondents),
                _ => throw new InvalidOperationException("Unknown Statistics question type.")
            };
            var type = question.Type switch
            {
                QuestionType.Text => "text", QuestionType.Number => "number", QuestionType.SingleChoice => "single_choice",
                QuestionType.MultipleChoice => "multiple_choice", QuestionType.Rating => "rating",
                _ => throw new InvalidOperationException("Unknown Statistics question type.")
            };
            return new StatisticsQuestionResponse(question.Id, question.Title, question.Description, type,
                values.Select(answer => answer.UserId).Distinct().Count(), items);
        }).ToArray();
    }

    private static object[] Choice(StatisticsQuestion question, IEnumerable<StatisticsOption> options,
        StatisticsAnswer[] answers, IReadOnlyDictionary<Guid, StatisticsRespondent>? respondents)
    {
        var configured = options.ToArray();
        var hits = answers.SelectMany(answer => question.Type == QuestionType.SingleChoice
            ? new[] { (Option: answer.Value.GetGuid(), answer.UserId) }
            : answer.Value.EnumerateArray().Select(value => (Option: value.GetGuid(), answer.UserId)))
            .ToLookup(hit => hit.Option, hit => hit.UserId);
        var optionIds = configured.Select(option => option.Id).ToHashSet();
        if (hits.Any(group => !optionIds.Contains(group.Key)))
            throw new InvalidOperationException("Invalid persisted choice.");
        return configured.Select(option =>
        {
            var users = hits[option.Id].Distinct().ToArray();
            return (object)new ChoiceStatisticsItem(option.Id, option.Label, option.Value, users.Length, Names(users, respondents));
        }).ToArray();
    }

    private static object[] Rating(StatisticsQuestion question, StatisticsAnswer[] answers,
        IReadOnlyDictionary<Guid, StatisticsRespondent>? respondents)
    {
        var min = question.Config.GetProperty("min").GetInt32();
        var max = question.Config.GetProperty("max").GetInt32();
        if (min < 0 || max > 100 || min >= max)
            throw new InvalidOperationException("Invalid persisted rating configuration.");
        var hits = answers.ToLookup(answer => ReadNumber(answer.Value), answer => answer.UserId);
        var range = Enumerable.Range(min, checked(max - min + 1)).Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
        var keys = range.Select(ReadNumber).ToHashSet();
        if (hits.Any(group => !keys.Contains(group.Key)))
            throw new InvalidOperationException("Invalid persisted rating.");
        return range.Select(json =>
        {
            return (object)Value(json, hits[ReadNumber(json)], respondents);
        }).ToArray();
    }

    private static DefinitionNumber ReadNumber(JsonElement value) =>
        DefinitionNumber.TryRead(value, out var number) ? number : throw new InvalidOperationException("Invalid persisted number.");

    private static ValueStatisticsItem Value(JsonElement value, IEnumerable<Guid> hits,
        IReadOnlyDictionary<Guid, StatisticsRespondent>? respondents)
    {
        var users = hits.Distinct().ToArray();
        return new(value.Clone(), users.Length, Names(users, respondents));
    }

    private static StatisticsRespondent[]? Names(IEnumerable<Guid> users,
        IReadOnlyDictionary<Guid, StatisticsRespondent>? respondents) => respondents is null ? null : users.Select(id => respondents[id])
            .OrderBy(user => user.DisplayName, StringComparer.Ordinal).ThenBy(user => user.Id).ToArray();
}
