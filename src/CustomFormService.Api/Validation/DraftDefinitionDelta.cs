using System.Text.Json;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Validation;

public static class DraftDefinitionDelta
{
    private static readonly HashSet<string> QuestionProperties =
        new(["id", "type", "title", "description", "required", "sortOrder", "config", "options"], StringComparer.Ordinal);

    private static readonly HashSet<string> OptionProperties =
        new(["id", "label", "value", "sortOrder"], StringComparer.Ordinal);

    public static bool TryApplyQuestions(
        FormDefinitionCandidate form,
        JsonElement questionsElement,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        if (questionsElement.ValueKind != JsonValueKind.Array)
        {
            errors = [Invalid("questions", "Questions must be an array.")];
            return false;
        }
        // Full shape pass -> ownership -> duplicate IDs, before candidate mutation.
        if (!TryReadDeltaItems(questionsElement, validationErrors, out var deltas) ||
            !TryValidateOwnership(form, deltas, validationErrors) ||
            !TryValidateDuplicates(deltas, validationErrors))
        {
            errors = validationErrors;
            return false;
        }
        foreach (var delta in deltas)
        {
            var properties = delta.Properties;
            var isNew = !properties.TryGetValue("id", out var idElement);
            QuestionDefinitionCandidate question;
            if (isNew)
            {
                question = new() { Id = Guid.NewGuid(), Config = EmptyObject };
                form.Questions.Add(question);
            }
            else
            {
                _ = TryReadId(idElement, $"{delta.Path}.id", validationErrors, out var id);
                question = form.Questions.Single(item => item.Id == id);
                if (properties.Count == 1)
                {
                    form.Questions.Remove(question);
                    continue;
                }
            }
            _ = TryReadQuestionValues(properties, delta.Path, validationErrors, out var values, question);
            var typeChanged = !isNew && question.Type != values.Type;
            foreach (var field in properties.Keys.Where(field => field != "id"))
                question.Source.Set(field, $"{delta.Path}.{field}",
                    field != "sortOrder" || isNew || question.SortOrder != values.SortOrder);
            if (typeChanged && !IsChoice(values.Type) && values.Options is { } optionDelta && optionDelta.GetArrayLength() > 0)
            {
                errors = [new($"{delta.Path}.options", "QUESTION_TYPE_CONFLICT", "Non-choice questions cannot receive option deltas.")];
                return false;
            }
            question.Type = values.Type;
            question.Title = values.Title;
            question.Description = values.Description;
            question.Required = values.Required;
            question.SortOrder = values.SortOrder;
            question.Config = typeChanged && !properties.ContainsKey("config") ? EmptyObject : values.Config;
            if (typeChanged)
            {
                if (!properties.ContainsKey("config")) question.Source.Set("config", $"{delta.Path}.type");
                if (!IsChoice(values.Type) || !IsChoice(values.OriginalType)) question.Options.Clear();
            }
            if (values.Options is { } options) ApplyOptions(question, options, $"{delta.Path}.options");
        }
        errors = [];
        return true;
    }

    private static void ApplyOptions(QuestionDefinitionCandidate question, JsonElement options, string path)
    {
        foreach (var item in options.EnumerateArray().Select((element, index) => (element, index)))
        {
            var properties = item.element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var itemPath = $"{path}[{item.index}]";
            var isNew = !properties.TryGetValue("id", out var idElement);
            OptionDefinitionCandidate option;
            if (isNew)
            {
                option = new() { Id = Guid.NewGuid() };
                question.Options.Add(option);
            }
            else
            {
                _ = TryReadId(idElement, $"{itemPath}.id", [], out var id);
                option = question.Options.Single(current => current.Id == id);
                if (properties.Count == 1)
                {
                    question.Options.Remove(option);
                    continue;
                }
            }
            _ = TryReadOptionValues(properties, itemPath, [], out var values, option);
            foreach (var field in properties.Keys.Where(field => field != "id"))
            {
                var changed = isNew || (field switch
                {
                    "value" => option.Value != values.Value,
                    "sortOrder" => option.SortOrder != values.SortOrder,
                    "label" => option.Label != TextRules.Trim(values.Label),
                    _ => true
                });
                option.Source.Set(field, $"{itemPath}.{field}", changed);
            }
            option.Label = values.Label;
            option.Value = values.Value;
            option.SortOrder = values.SortOrder;
        }
    }

    private static bool TryReadDeltaItems(
        JsonElement questionsElement,
        List<ApiErrorDetail> errors,
        out IReadOnlyList<QuestionDelta> deltas)
    {
        var result = new List<QuestionDelta>();
        var index = 0;
        foreach (var item in questionsElement.EnumerateArray())
        {
            var path = $"questions[{index}]";
            if (!TryReadObject(item, QuestionProperties, path, errors, out var properties) ||
                !TryValidateQuestionShape(properties, path, errors))
            {
                deltas = [];
                return false;
            }

            result.Add(new QuestionDelta(path, properties));
            index++;
        }

        deltas = result;
        return true;
    }

    private static bool TryValidateQuestionShape(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors)
    {
        if (properties.TryGetValue("id", out var idElement) &&
            !TryReadId(idElement, $"{path}.id", errors, out _))
        {
            return false;
        }

        if (!properties.ContainsKey("id") &&
            (!Require(properties, "type", path, errors) ||
             !Require(properties, "title", path, errors) ||
             !Require(properties, "sortOrder", path, errors)))
        {
            return false;
        }

        if ((properties.Count > 1 || !properties.ContainsKey("id")) &&
            !TryReadQuestionValues(properties, path, errors, out _))
        {
            return false;
        }

        if (!properties.TryGetValue("options", out var options))
        {
            return true;
        }

        var optionIndex = 0;
        foreach (var option in options.EnumerateArray())
        {
            var optionPath = $"{path}.options[{optionIndex}]";
            if (!TryReadObject(option, OptionProperties, optionPath, errors, out var optionProperties) ||
                !TryValidateOptionShape(optionProperties, optionPath, errors))
            {
                return false;
            }

            optionIndex++;
        }

        return true;
    }

    private static bool TryValidateOptionShape(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors)
    {
        if (properties.TryGetValue("id", out var idElement) &&
            !TryReadId(idElement, $"{path}.id", errors, out _))
        {
            return false;
        }

        if (!properties.ContainsKey("id") &&
            (!Require(properties, "label", path, errors) ||
             !Require(properties, "value", path, errors) ||
             !Require(properties, "sortOrder", path, errors)))
        {
            return false;
        }

        return (properties.Count == 1 && properties.ContainsKey("id")) ||
               TryReadOptionValues(properties, path, errors, out _);
    }

    private static bool TryValidateOwnership(
        FormDefinitionCandidate form,
        IReadOnlyList<QuestionDelta> deltas,
        List<ApiErrorDetail> errors)
    {
        foreach (var delta in deltas)
        {
            var properties = delta.Properties;
            if (!properties.TryGetValue("id", out var idElement))
            {
                if (properties.TryGetValue("options", out var newQuestionOptions))
                {
                    foreach (var option in newQuestionOptions.EnumerateArray().Select((element, index) => (element, index)))
                    {
                        if (option.element.TryGetProperty("id", out _))
                        {
                            errors.Add(new ApiErrorDetail(
                                $"{delta.Path}.options[{option.index}].id",
                                "OPTION_NOT_FOUND",
                                "The option was not found in this question."));
                            return false;
                        }
                    }
                }

                continue;
            }

            _ = TryReadId(idElement, $"{delta.Path}.id", errors, out var questionId);
            if (form.Questions.SingleOrDefault(question => question.Id == questionId) is not { } question)
            {
                errors.Add(new ApiErrorDetail($"{delta.Path}.id", "QUESTION_NOT_FOUND", "The question was not found in this form."));
                return false;
            }

            if (!properties.TryGetValue("options", out var options))
            {
                continue;
            }

            foreach (var option in options.EnumerateArray().Select((element, index) => (element, index)))
            {
                if (!option.element.TryGetProperty("id", out var optionIdElement))
                {
                    continue;
                }

                _ = TryReadId(optionIdElement, $"{delta.Path}.options[{option.index}].id", errors, out var optionId);
                if (question.Options.All(currentOption => currentOption.Id != optionId))
                {
                    errors.Add(new ApiErrorDetail(
                        $"{delta.Path}.options[{option.index}].id",
                        "OPTION_NOT_FOUND",
                        "The option was not found in this question."));
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryValidateDuplicates(
        IReadOnlyList<QuestionDelta> deltas,
        List<ApiErrorDetail> errors)
    {
        var questionIds = new HashSet<Guid>();
        foreach (var delta in deltas)
        {
            if (delta.Properties.TryGetValue("id", out var questionIdElement))
            {
                _ = TryReadId(questionIdElement, $"{delta.Path}.id", errors, out var questionId);
                if (!questionIds.Add(questionId))
                {
                    errors.Add(Invalid($"{delta.Path}.id", "Question IDs must not be repeated within a patch."));
                    return false;
                }
            }

            if (!delta.Properties.TryGetValue("options", out var options))
            {
                continue;
            }

            var optionIds = new HashSet<Guid>();
            foreach (var option in options.EnumerateArray().Select((element, index) => (element, index)))
            {
                if (!option.element.TryGetProperty("id", out var optionIdElement))
                {
                    continue;
                }

                _ = TryReadId(optionIdElement, $"{delta.Path}.options[{option.index}].id", errors, out var optionId);
                if (!optionIds.Add(optionId))
                {
                    errors.Add(Invalid($"{delta.Path}.options[{option.index}].id", "Option IDs must not be repeated within a patch."));
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryReadQuestionValues(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors,
        out QuestionValues values,
        QuestionDefinitionCandidate? existing = null)
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
                if (!JsonElement.DeepEquals(config, configElement)) config = configElement.Clone();
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

    private static bool TryReadOptionValues(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors,
        out OptionValues values,
        OptionDefinitionCandidate? existing = null)
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
                // Establish the normalized alternate key before tracking a new Option
                // or deciding whether an existing key needs a same-ID replacement.
                value = TextRules.Trim(valueElement.GetString()!);
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

    private static ApiErrorDetail Invalid(string path, string message) =>
        new(path, "INVALID_VALUE", message);

    private static JsonElement EmptyObject => JsonDocument.Parse("{}").RootElement.Clone();

    private readonly record struct QuestionValues(
        QuestionType Type,
        QuestionType OriginalType,
        string Title,
        string? Description,
        bool Required,
        int SortOrder,
        JsonElement Config,
        JsonElement? Options);

    private sealed record QuestionDelta(string Path, IReadOnlyDictionary<string, JsonElement> Properties);

    private readonly record struct OptionValues(string Label, string Value, int SortOrder);
}
