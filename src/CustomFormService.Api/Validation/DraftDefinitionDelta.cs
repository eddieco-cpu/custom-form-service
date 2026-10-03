using System.Text.Json;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Validation;

public static class DraftDefinitionDelta
{
    private static readonly HashSet<string> QuestionProperties =
        new(["id", "type", "title", "description", "required", "sortOrder", "config", "options"], StringComparer.Ordinal);

    private static readonly HashSet<string> OptionProperties =
        new(["id", "label", "value", "sortOrder"], StringComparer.Ordinal);

    public static DefinitionSnapshot Capture(Form form) => new(form);

    public static bool TryApplyQuestions(
        DbContext database,
        Form form,
        JsonElement questionsElement,
        DateTimeOffset now,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        if (questionsElement.ValueKind != JsonValueKind.Array)
        {
            errors = [Invalid("questions", "Questions must be an array.")];
            return false;
        }

        var seenQuestionIds = new HashSet<Guid>();
        var index = 0;
        foreach (var delta in questionsElement.EnumerateArray())
        {
            var path = $"questions[{index}]";
            if (!TryReadObject(delta, QuestionProperties, path, validationErrors, out var properties))
            {
                errors = validationErrors;
                return false;
            }

            if (!properties.TryGetValue("id", out var idElement))
            {
                if (!TryCreateQuestion(database, form, properties, path, now, validationErrors))
                {
                    errors = validationErrors;
                    return false;
                }
            }
            else if (!TryReadId(idElement, $"{path}.id", validationErrors, out var questionId))
            {
                errors = validationErrors;
                return false;
            }
            else if (!seenQuestionIds.Add(questionId))
            {
                validationErrors.Add(Invalid($"{path}.id", "Question IDs must not be repeated within a patch."));
                errors = validationErrors;
                return false;
            }
            else if (form.Questions.SingleOrDefault(question => question.Id == questionId) is not { } question)
            {
                validationErrors.Add(new ApiErrorDetail($"{path}.id", "QUESTION_NOT_FOUND", "The question was not found in this form."));
                errors = validationErrors;
                return false;
            }
            else if (properties.Count == 1)
            {
                database.Remove(question);
                form.Questions.Remove(question);
            }
            else if (!TryUpdateQuestion(database, question, properties, path, now, validationErrors))
            {
                errors = validationErrors;
                return false;
            }

            index++;
        }

        var currentQuestions = form.Questions
            .Where(question => database.Entry(question).State != EntityState.Deleted)
            .ToArray();
        using var candidateDocument = JsonDocument.Parse(JsonSerializer.Serialize(currentQuestions.Select(question => new
        {
            type = ToApiValue(question.Type),
            title = question.Title,
            description = question.Description,
            required = question.Required,
            sortOrder = question.SortOrder,
            config = question.Config,
            options = question.Options
                .Where(option => database.Entry(option).State != EntityState.Deleted)
                .Select(option => new { label = option.Label, value = option.Value, sortOrder = option.SortOrder })
                .ToArray()
        })));
        if (!FormDefinitionValidator.TryCreateQuestions(candidateDocument.RootElement, out var definitions, out var definitionErrors))
        {
            errors = definitionErrors;
            return false;
        }

        for (var questionIndex = 0; questionIndex < currentQuestions.Length; questionIndex++)
        {
            var question = currentQuestions[questionIndex];
            var definition = definitions[questionIndex];
            question.Type = definition.Type;
            question.Title = definition.Title;
            question.Description = definition.Description;
            question.Required = definition.Required;
            question.SortOrder = definition.SortOrder;
            question.Config = definition.Config;

            var currentOptions = question.Options
                .Where(option => database.Entry(option).State != EntityState.Deleted)
                .ToArray();
            for (var optionIndex = 0; optionIndex < currentOptions.Length; optionIndex++)
            {
                var option = currentOptions[optionIndex];
                var optionDefinition = definition.Options[optionIndex];
                option.Label = optionDefinition.Label;
                option.Value = optionDefinition.Value;
                option.SortOrder = optionDefinition.SortOrder;
            }
        }

        errors = [];
        return true;
    }

    private static bool TryCreateQuestion(
        DbContext database,
        Form form,
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        DateTimeOffset now,
        List<ApiErrorDetail> errors)
    {
        if (!Require(properties, "type", path, errors) ||
            !Require(properties, "title", path, errors) ||
            !Require(properties, "sortOrder", path, errors) ||
            !TryReadQuestionValues(properties, path, errors, out var values))
        {
            return false;
        }

        var question = new Question
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            Type = values.Type,
            Title = values.Title,
            Description = values.Description,
            Required = values.Required,
            SortOrder = values.SortOrder,
            Config = values.Config,
            CreatedAt = now,
            UpdatedAt = now
        };
        form.Questions.Add(question);
        database.Add(question);
        return TryApplyOptions(database, question, values.Options.GetValueOrDefault(EmptyArray), $"{path}.options", now, errors);
    }

    private static bool TryUpdateQuestion(
        DbContext database,
        Question question,
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        DateTimeOffset now,
        List<ApiErrorDetail> errors)
    {
        if (!TryReadQuestionValues(properties, path, errors, out var values, question))
        {
            return false;
        }

        var typeChanged = question.Type != values.Type;
        question.Type = values.Type;
        question.Title = values.Title;
        question.Description = values.Description;
        question.Required = values.Required;
        question.SortOrder = values.SortOrder;
        question.Config = values.Config;

        if (typeChanged)
        {
            if (!properties.ContainsKey("config"))
            {
                question.Config = EmptyObject;
            }

            if (!IsChoice(question.Type))
            {
                if (values.Options is { ValueKind: JsonValueKind.Array } optionDelta && optionDelta.GetArrayLength() > 0)
                {
                    errors.Add(new ApiErrorDetail($"{path}.options", "QUESTION_TYPE_CONFLICT", "Non-choice questions cannot receive option deltas."));
                    return false;
                }

                foreach (var option in question.Options.ToArray())
                {
                    database.Remove(option);
                    question.Options.Remove(option);
                }
            }
            else if (!IsChoice(values.OriginalType))
            {
                foreach (var option in question.Options.ToArray())
                {
                    database.Remove(option);
                    question.Options.Remove(option);
                }
            }
        }

        return values.Options is null ||
            TryApplyOptions(database, question, values.Options.GetValueOrDefault(), $"{path}.options", now, errors);
    }

    private static bool TryReadQuestionValues(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors,
        out QuestionValues values,
        Question? existing = null)
    {
        var type = existing?.Type ?? default;
        var title = existing?.Title ?? string.Empty;
        var description = existing?.Description;
        var required = existing?.Required ?? false;
        var sortOrder = existing?.SortOrder ?? 0;
        var config = existing?.Config ?? EmptyObject;
        JsonElement? options = null;

        if (properties.TryGetValue("type", out var typeElement))
        {
            if (!TryReadQuestionType(typeElement, $"{path}.type", errors, out type))
            {
                values = default;
                return false;
            }
        }
        if (properties.TryGetValue("title", out var titleElement))
        {
            if (titleElement.ValueKind != JsonValueKind.String)
            {
                errors.Add(Invalid($"{path}.title", "Title must be a non-null string."));
            }
            else
            {
                title = titleElement.GetString()!;
            }
        }
        if (properties.TryGetValue("description", out var descriptionElement))
        {
            if (descriptionElement.ValueKind == JsonValueKind.Null)
            {
                description = null;
            }
            else if (descriptionElement.ValueKind == JsonValueKind.String)
            {
                description = descriptionElement.GetString();
            }
            else
            {
                errors.Add(Invalid($"{path}.description", "Description must be a string or null."));
            }
        }
        if (properties.TryGetValue("required", out var requiredElement))
        {
            if (requiredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                errors.Add(Invalid($"{path}.required", "Required must be a boolean."));
            }
            else
            {
                required = requiredElement.GetBoolean();
            }
        }
        if (properties.TryGetValue("sortOrder", out var sortOrderElement))
        {
            if (sortOrderElement.ValueKind != JsonValueKind.Number || !sortOrderElement.TryGetInt32(out sortOrder) || sortOrder < 0)
            {
                errors.Add(new ApiErrorDetail($"{path}.sortOrder", "INVALID_SORT_ORDER", "sortOrder must be a non-negative integer."));
            }
        }
        if (properties.TryGetValue("config", out var configElement))
        {
            if (configElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ApiErrorDetail($"{path}.config", "INVALID_QUESTION_CONFIG", "Config must be a JSON object."));
            }
            else
            {
                config = configElement.Clone();
            }
        }
        if (properties.TryGetValue("options", out var optionsElement))
        {
            if (optionsElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add(Invalid($"{path}.options", "Options must be an array."));
            }
            else
            {
                options = optionsElement;
            }
        }

        if (errors.Count > 0)
        {
            values = default;
            return false;
        }

        values = new QuestionValues(type, existing?.Type ?? type, title, description, required, sortOrder, config, options);
        return true;
    }

    private static bool TryApplyOptions(
        DbContext database,
        Question question,
        JsonElement optionsElement,
        string path,
        DateTimeOffset now,
        List<ApiErrorDetail> errors)
    {
        var seenOptionIds = new HashSet<Guid>();
        var index = 0;
        foreach (var delta in optionsElement.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            if (!TryReadObject(delta, OptionProperties, itemPath, errors, out var properties))
            {
                return false;
            }

            if (!properties.TryGetValue("id", out var idElement))
            {
                if (!Require(properties, "label", itemPath, errors) ||
                    !Require(properties, "value", itemPath, errors) ||
                    !Require(properties, "sortOrder", itemPath, errors) ||
                    !TryReadOptionValues(properties, itemPath, errors, out var values))
                {
                    return false;
                }

                var option = new Option
                {
                    Id = Guid.NewGuid(),
                    QuestionId = question.Id,
                    Label = values.Label,
                    Value = values.Value,
                    SortOrder = values.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                question.Options.Add(option);
                database.Add(option);
            }
            else if (!TryReadId(idElement, $"{itemPath}.id", errors, out var optionId))
            {
                return false;
            }
            else if (!seenOptionIds.Add(optionId))
            {
                errors.Add(Invalid($"{itemPath}.id", "Option IDs must not be repeated within a patch."));
                return false;
            }
            else if (question.Options.SingleOrDefault(option => option.Id == optionId) is not { } option)
            {
                errors.Add(new ApiErrorDetail($"{itemPath}.id", "OPTION_NOT_FOUND", "The option was not found in this question."));
                return false;
            }
            else if (properties.Count == 1)
            {
                database.Remove(option);
                question.Options.Remove(option);
            }
            else if (!TryReadOptionValues(properties, itemPath, errors, out var values, option))
            {
                return false;
            }
            else
            {
                option.Label = values.Label;
                option.Value = values.Value;
                option.SortOrder = values.SortOrder;
            }

            index++;
        }

        return true;
    }

    private static bool TryReadOptionValues(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors,
        out OptionValues values,
        Option? existing = null)
    {
        var label = existing?.Label ?? string.Empty;
        var value = existing?.Value ?? string.Empty;
        var sortOrder = existing?.SortOrder ?? 0;
        if (properties.TryGetValue("label", out var labelElement))
        {
            if (labelElement.ValueKind != JsonValueKind.String)
            {
                errors.Add(Invalid($"{path}.label", "Label must be a non-null string."));
            }
            else
            {
                label = labelElement.GetString()!;
            }
        }
        if (properties.TryGetValue("value", out var valueElement))
        {
            if (valueElement.ValueKind != JsonValueKind.String)
            {
                errors.Add(Invalid($"{path}.value", "Value must be a non-null string."));
            }
            else
            {
                value = valueElement.GetString()!;
            }
        }
        if (properties.TryGetValue("sortOrder", out var sortOrderElement) &&
            (sortOrderElement.ValueKind != JsonValueKind.Number || !sortOrderElement.TryGetInt32(out sortOrder) || sortOrder < 0))
        {
            errors.Add(new ApiErrorDetail($"{path}.sortOrder", "INVALID_SORT_ORDER", "sortOrder must be a non-negative integer."));
        }

        values = new OptionValues(label, value, sortOrder);
        return errors.Count == 0;
    }

    private static bool TryReadObject(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string path,
        List<ApiErrorDetail> errors,
        out Dictionary<string, JsonElement> properties)
    {
        properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Invalid(path, "This item must be a JSON object."));
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (!allowed.Contains(property.Name))
            {
                errors.Add(Invalid(propertyPath, "This field is not allowed."));
            }
            else if (!properties.TryAdd(property.Name, property.Value))
            {
                errors.Add(Invalid(propertyPath, "This field must not be repeated."));
            }
        }

        return errors.Count == 0;
    }

    private static bool Require(IReadOnlyDictionary<string, JsonElement> properties, string name, string path, List<ApiErrorDetail> errors)
    {
        if (properties.ContainsKey(name))
        {
            return true;
        }

        errors.Add(new ApiErrorDetail($"{path}.{name}", "REQUIRED_FIELD", "This field is required."));
        return false;
    }

    private static bool TryReadId(JsonElement element, string path, List<ApiErrorDetail> errors, out Guid id)
    {
        if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out id))
        {
            return true;
        }

        id = default;
        errors.Add(Invalid(path, "ID must be a UUID."));
        return false;
    }

    private static bool TryReadQuestionType(JsonElement element, string path, List<ApiErrorDetail> errors, out QuestionType type)
    {
        type = element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
            {
                "text" => QuestionType.Text,
                "number" => QuestionType.Number,
                "rating" => QuestionType.Rating,
                "single_choice" => QuestionType.SingleChoice,
                "multiple_choice" => QuestionType.MultipleChoice,
                _ => default
            }
            : default;
        if (element.ValueKind == JsonValueKind.String && element.GetString() is "text" or "number" or "rating" or "single_choice" or "multiple_choice")
        {
            return true;
        }

        errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_TYPE", "Question type is invalid."));
        return false;
    }

    private static bool IsChoice(QuestionType type) =>
        type is QuestionType.SingleChoice or QuestionType.MultipleChoice;

    private static string ToApiValue(QuestionType type) => type switch
    {
        QuestionType.Text => "text",
        QuestionType.Number => "number",
        QuestionType.Rating => "rating",
        QuestionType.SingleChoice => "single_choice",
        QuestionType.MultipleChoice => "multiple_choice",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static ApiErrorDetail Invalid(string path, string message) =>
        new(path, "INVALID_VALUE", message);

    private static JsonElement EmptyObject => JsonDocument.Parse("{}").RootElement.Clone();

    private static JsonElement EmptyArray => JsonDocument.Parse("[]").RootElement.Clone();

    private readonly record struct QuestionValues(
        QuestionType Type,
        QuestionType OriginalType,
        string Title,
        string? Description,
        bool Required,
        int SortOrder,
        JsonElement Config,
        JsonElement? Options);

    private readonly record struct OptionValues(string Label, string Value, int SortOrder);
}

public sealed class DefinitionSnapshot
{
    private readonly string _formDefinition;
    private readonly IReadOnlyDictionary<Guid, string> _questions;
    private readonly IReadOnlyDictionary<Guid, string> _options;

    internal DefinitionSnapshot(Form form)
    {
        _formDefinition = SerializeForm(form);
        _questions = form.Questions.ToDictionary(question => question.Id, SerializeQuestion);
        _options = form.Questions.SelectMany(question => question.Options).ToDictionary(option => option.Id, SerializeOption);
    }

    public bool IsChanged(Form form) => !string.Equals(_formDefinition, SerializeForm(form), StringComparison.Ordinal);

    public void UpdateChangedTimestamps(DbContext database, Form form, DateTimeOffset now)
    {
        foreach (var question in form.Questions.Where(question => database.Entry(question).State != EntityState.Deleted))
        {
            if (!_questions.TryGetValue(question.Id, out var before) || !string.Equals(before, SerializeQuestion(question), StringComparison.Ordinal))
            {
                question.UpdatedAt = now;
            }
        }

        foreach (var option in form.Questions
                     .Where(question => database.Entry(question).State != EntityState.Deleted)
                     .SelectMany(question => question.Options)
                     .Where(option => database.Entry(option).State != EntityState.Deleted))
        {
            if (!_options.TryGetValue(option.Id, out var before) || !string.Equals(before, SerializeOption(option), StringComparison.Ordinal))
            {
                option.UpdatedAt = now;
            }
        }
    }

    private static string SerializeForm(Form form) => JsonSerializer.Serialize(new
    {
        form.Title,
        form.Description,
        form.ResponseMode,
        form.ResponseIdentity,
        form.SubmissionDeadlineAt,
        questions = form.Questions
            .Where(question => question.FormId == form.Id)
            .OrderBy(question => question.Id)
            .Select(SerializeQuestion)
            .ToArray()
    });

    private static string SerializeQuestion(Question question) => JsonSerializer.Serialize(new
    {
        question.Id,
        question.Type,
        question.Title,
        question.Description,
        question.Required,
        question.SortOrder,
        question.Config,
        options = question.Options.OrderBy(option => option.Id).Select(SerializeOption).ToArray()
    });

    private static string SerializeOption(Option option) => JsonSerializer.Serialize(new
    {
        option.Id,
        option.Label,
        option.Value,
        option.SortOrder
    });
}
