using System.Text.Json;
using System.Text.Json.Nodes;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Validation;

public static class FormDefinitionValidator
{
    private static readonly HashSet<string> QuestionProperties =
        new(["type", "title", "description", "required", "sortOrder", "config", "options"], StringComparer.Ordinal);

    private static readonly HashSet<string> RequiredQuestionProperties =
        new(["type", "title", "sortOrder"], StringComparer.Ordinal);

    private static readonly HashSet<string> OptionProperties =
        new(["label", "value", "sortOrder"], StringComparer.Ordinal);

    private static readonly HashSet<string> RequiredOptionProperties =
        new(["label", "value", "sortOrder"], StringComparer.Ordinal);

    public static bool TryCreateQuestions(
        JsonElement questionsElement,
        out IReadOnlyList<NewQuestionDefinition> questions,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        var definitions = new List<NewQuestionDefinition>();

        if (questionsElement.ValueKind != JsonValueKind.Array)
        {
            questions = [];
            errors = [Invalid("questions", "Questions must be an array.")];
            return false;
        }

        var questionSortOrders = new Dictionary<int, int>();
        var index = 0;
        foreach (var questionElement in questionsElement.EnumerateArray())
        {
            var path = $"questions[{index}]";
            if (TryCreateQuestion(questionElement, path, out var definition, out var questionErrors))
            {
                if (questionSortOrders.TryGetValue(definition.SortOrder, out _))
                {
                    validationErrors.Add(new ApiErrorDetail(
                        $"{path}.sortOrder",
                        "DUPLICATE_SORT_ORDER",
                        "Question sortOrder values must be unique within a form."));
                }
                else
                {
                    questionSortOrders.Add(definition.SortOrder, index);
                }

                definitions.Add(definition);
            }

            validationErrors.AddRange(questionErrors);
            index++;
        }

        questions = definitions;
        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    private static bool TryCreateQuestion(
        JsonElement questionElement,
        string path,
        out NewQuestionDefinition definition,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        definition = default!;
        if (!TryReadObject(
                questionElement,
                QuestionProperties,
                RequiredQuestionProperties,
                path,
                validationErrors,
                out var properties))
        {
            errors = validationErrors;
            return false;
        }

        var hasType = TryReadQuestionType(properties, $"{path}.type", validationErrors, out var type);
        var hasTitle = TryReadRequiredText(properties, "title", $"{path}.title", validationErrors, out var title);
        var hasSortOrder = TryReadSortOrder(properties, "sortOrder", $"{path}.sortOrder", validationErrors, out var sortOrder);
        var description = ReadNullableDescription(properties, "description", $"{path}.description", validationErrors);
        var required = ReadRequired(properties, $"{path}.required", validationErrors);

        var configElement = EmptyObject;
        if (properties.TryGetValue("config", out var suppliedConfig))
        {
            configElement = suppliedConfig;
        }

        var config = EmptyObject;
        var hasConfig = hasType && TryNormalizeConfig(type, configElement, $"{path}.config", validationErrors, out config);

        var optionDefinitions = Array.Empty<NewOptionDefinition>();
        if (properties.TryGetValue("options", out var optionsElement))
        {
            if (optionsElement.ValueKind != JsonValueKind.Array)
            {
                validationErrors.Add(Invalid($"{path}.options", "Options must be an array."));
            }
            else if (hasType && !IsChoice(type) && optionsElement.GetArrayLength() > 0)
            {
                validationErrors.Add(new ApiErrorDetail(
                    $"{path}.options",
                    "INVALID_OPTION",
                    "Only choice questions may contain options."));
            }
            else if (hasType && IsChoice(type))
            {
                TryCreateOptions(optionsElement, $"{path}.options", validationErrors, out optionDefinitions);
            }
        }

        if (!hasType || !hasTitle || !hasSortOrder || !hasConfig || validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        definition = new NewQuestionDefinition(type, title, description, required, sortOrder, config, optionDefinitions);
        errors = [];
        return true;
    }

    private static void TryCreateOptions(
        JsonElement optionsElement,
        string optionsPath,
        List<ApiErrorDetail> validationErrors,
        out NewOptionDefinition[] options)
    {
        var definitions = new List<NewOptionDefinition>();
        var optionSortOrders = new HashSet<int>();
        var optionValues = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var optionElement in optionsElement.EnumerateArray())
        {
            var path = $"{optionsPath}[{index}]";
            var itemErrors = new List<ApiErrorDetail>();
            if (TryReadObject(
                    optionElement,
                    OptionProperties,
                    RequiredOptionProperties,
                    path,
                    itemErrors,
                    out var properties))
            {
                var hasLabel = TryReadRequiredText(properties, "label", $"{path}.label", itemErrors, out var label);
                var hasValue = TryReadRequiredText(properties, "value", $"{path}.value", itemErrors, out var value);
                if (hasValue && TextRules.CodePointCount(value) > 255)
                {
                    itemErrors.Add(new ApiErrorDetail(
                        $"{path}.value",
                        "INVALID_OPTION",
                        "Option value must not exceed 255 characters."));
                }

                var hasSortOrder = TryReadSortOrder(properties, "sortOrder", $"{path}.sortOrder", itemErrors, out var sortOrder);
                if (hasSortOrder && !optionSortOrders.Add(sortOrder))
                {
                    itemErrors.Add(new ApiErrorDetail(
                        $"{path}.sortOrder",
                        "DUPLICATE_SORT_ORDER",
                        "Option sortOrder values must be unique within a question."));
                }

                if (hasValue && !optionValues.Add(value))
                {
                    itemErrors.Add(new ApiErrorDetail(
                        $"{path}.value",
                        "INVALID_OPTION",
                        "Option values must be unique within a question."));
                }

                if (hasLabel && hasValue && hasSortOrder && itemErrors.Count == 0)
                {
                    definitions.Add(new NewOptionDefinition(label, value, sortOrder));
                }
            }

            validationErrors.AddRange(itemErrors);
            index++;
        }

        options = [.. definitions];
    }

    private static bool TryNormalizeConfig(
        QuestionType type,
        JsonElement configElement,
        string path,
        List<ApiErrorDetail> errors,
        out JsonElement config)
    {
        config = EmptyObject;
        if (configElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_CONFIG", "Config must be a JSON object."));
            return false;
        }

        var allowed = type switch
        {
            QuestionType.Text => new HashSet<string>(["variant", "placeholder", "minLength", "maxLength"], StringComparer.Ordinal),
            QuestionType.Number => new HashSet<string>(["placeholder", "min", "max"], StringComparer.Ordinal),
            QuestionType.SingleChoice => new HashSet<string>(["variant"], StringComparer.Ordinal),
            QuestionType.MultipleChoice => new HashSet<string>(["variant"], StringComparer.Ordinal),
            QuestionType.Rating => new HashSet<string>(["variant", "min", "max"], StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var result = new JsonObject();
        foreach (var property in configElement.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (!allowed.Contains(property.Name))
            {
                errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "This config property is not allowed for the question type."));
                continue;
            }

            if (result.ContainsKey(property.Name))
            {
                errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "This config property must not be repeated."));
                continue;
            }

            switch (property.Name)
            {
                case "variant":
                    if (property.Value.ValueKind != JsonValueKind.String || !IsValidVariant(type, property.Value.GetString()!))
                    {
                        errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "The configured variant is invalid."));
                    }
                    else
                    {
                        result[property.Name] = property.Value.GetString();
                    }

                    break;
                case "placeholder":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "Placeholder must be a string."));
                    }
                    else
                    {
                        result[property.Name] = TextRules.Trim(property.Value.GetString()!);
                    }

                    break;
                case "minLength":
                case "maxLength":
                    if (!TryReadNonnegativeInteger(property.Value, out var textLength))
                    {
                        errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "Text length must be a non-negative integer."));
                    }
                    else
                    {
                        result[property.Name] = textLength;
                    }

                    break;
                case "min":
                case "max":
                    if (type == QuestionType.Rating)
                    {
                        if (!TryReadRatingBound(property.Value, out var ratingBound))
                        {
                            errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "Rating bounds must be integers from 0 through 100."));
                        }
                        else
                        {
                            result[property.Name] = ratingBound;
                        }
                    }
                    else if (!DefinitionNumber.TryRead(property.Value, out _))
                    {
                        errors.Add(new ApiErrorDetail(propertyPath, "INVALID_QUESTION_CONFIG", "Number bounds must be finite JSON numbers."));
                    }
                    else
                    {
                        result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                    }

                    break;
            }
        }

        ValidateConfigRelationships(type, result, path, errors);
        if (errors.Any(error => error.Path == path || error.Path.StartsWith(path + ".", StringComparison.Ordinal)))
        {
            return false;
        }

        config = JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
        return true;
    }

    private static void ValidateConfigRelationships(
        QuestionType type,
        JsonObject config,
        string path,
        List<ApiErrorDetail> errors)
    {
        if (type == QuestionType.Text &&
            config["minLength"] is JsonValue minLength &&
            config["maxLength"] is JsonValue maxLength &&
            minLength.TryGetValue<int>(out var textMin) &&
            maxLength.TryGetValue<int>(out var textMax) &&
            textMin > textMax)
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_CONFIG", "minLength must not exceed maxLength."));
        }

        if (type == QuestionType.Number &&
            config["min"] is not null &&
            config["max"] is not null &&
            DefinitionNumber.TryRead(JsonSerializer.SerializeToElement(config["min"]!), out var numberMin) &&
            DefinitionNumber.TryRead(JsonSerializer.SerializeToElement(config["max"]!), out var numberMax) &&
            numberMin.CompareTo(numberMax) > 0)
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_CONFIG", "min must not exceed max."));
        }

        if (type == QuestionType.Rating &&
            config["min"] is JsonValue minValue &&
            config["max"] is JsonValue maxValue &&
            minValue.TryGetValue<int>(out var ratingMin) &&
            maxValue.TryGetValue<int>(out var ratingMax) &&
            ratingMin >= ratingMax)
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_CONFIG", "Rating min must be less than max."));
        }
    }

    private static bool TryReadQuestionType(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors,
        out QuestionType type)
    {
        type = default;
        if (!properties.TryGetValue("type", out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_TYPE", "Question type is invalid."));
            return false;
        }

        var value = element.GetString();
        type = value switch
        {
            "text" => QuestionType.Text,
            "number" => QuestionType.Number,
            "rating" => QuestionType.Rating,
            "single_choice" => QuestionType.SingleChoice,
            "multiple_choice" => QuestionType.MultipleChoice,
            _ => default
        };
        if (value is "text" or "number" or "rating" or "single_choice" or "multiple_choice")
        {
            return true;
        }

        errors.Add(new ApiErrorDetail(path, "INVALID_QUESTION_TYPE", "Question type is invalid."));
        return false;
    }

    private static bool TryReadRequiredText(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string path,
        List<ApiErrorDetail> errors,
        out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(name, out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(Invalid(path, "This field must be a non-null string."));
            return false;
        }

        value = TextRules.Trim(element.GetString()!);
        if (value.Length == 0)
        {
            errors.Add(new ApiErrorDetail(path, "REQUIRED_FIELD", "This field is required."));
            return false;
        }

        return true;
    }

    private static string? ReadNullableDescription(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string path,
        List<ApiErrorDetail> errors)
    {
        if (!properties.TryGetValue(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(Invalid(path, "Description must be a string or null."));
            return null;
        }

        var value = TextRules.Trim(element.GetString()!);
        return value.Length == 0 ? null : value;
    }

    private static bool ReadRequired(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        List<ApiErrorDetail> errors)
    {
        if (!properties.TryGetValue("required", out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            errors.Add(Invalid(path, "Required must be a boolean."));
            return false;
        }

        return element.GetBoolean();
    }

    private static bool TryReadSortOrder(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string path,
        List<ApiErrorDetail> errors,
        out int sortOrder)
    {
        sortOrder = default;
        if (!properties.TryGetValue(name, out var element))
        {
            return false;
        }

        if (!TryReadNonnegativeInteger(element, out sortOrder))
        {
            errors.Add(new ApiErrorDetail(path, "INVALID_SORT_ORDER", "sortOrder must be a non-negative integer."));
            return false;
        }

        return true;
    }

    private static bool TryReadObject(
        JsonElement element,
        IReadOnlySet<string> allowed,
        IReadOnlySet<string> required,
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
                continue;
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                errors.Add(Invalid(propertyPath, "This field must not be repeated."));
            }
        }

        foreach (var name in required)
        {
            if (!properties.ContainsKey(name))
            {
                errors.Add(new ApiErrorDetail($"{path}.{name}", "REQUIRED_FIELD", "This field is required."));
            }
        }

        return errors.Count == 0;
    }

    private static bool IsChoice(QuestionType type) =>
        type is QuestionType.SingleChoice or QuestionType.MultipleChoice;

    private static bool IsValidVariant(QuestionType type, string variant) => type switch
    {
        QuestionType.Text => variant is "input" or "textarea",
        QuestionType.SingleChoice => variant is "radio" or "select",
        QuestionType.MultipleChoice => variant is "checkbox" or "multi_select",
        QuestionType.Rating => variant is "stars" or "range",
        _ => false
    };

    private static bool TryReadNonnegativeInteger(JsonElement element, out int value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= 0;
    }

    private static bool TryReadRatingBound(JsonElement element, out int value) =>
        TryReadNonnegativeInteger(element, out value) && value <= 100;

    private static ApiErrorDetail Invalid(string path, string message) =>
        new(path, "INVALID_VALUE", message);

    private static JsonElement EmptyObject => JsonDocument.Parse("{}").RootElement.Clone();
}

public sealed record NewQuestionDefinition(
    QuestionType Type,
    string Title,
    string? Description,
    bool Required,
    int SortOrder,
    JsonElement Config,
    IReadOnlyList<NewOptionDefinition> Options);

public sealed record NewOptionDefinition(string Label, string Value, int SortOrder);
