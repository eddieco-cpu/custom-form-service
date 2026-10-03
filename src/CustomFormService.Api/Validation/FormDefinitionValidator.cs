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
        var candidates = new List<QuestionDefinitionCandidate>();

        if (questionsElement.ValueKind != JsonValueKind.Array)
        {
            questions = [];
            errors = [Invalid("questions", "Questions must be an array.")];
            return false;
        }

        var index = 0;
        foreach (var questionElement in questionsElement.EnumerateArray())
        {
            var path = $"questions[{index}]";
            if (TryCreateQuestion(questionElement, path, out var definition, out var questionErrors))
            {
                candidates.Add(definition);
            }

            validationErrors.AddRange(questionErrors);
            index++;
        }

        TryNormalizeQuestions(candidates, out var candidateErrors);
        validationErrors.AddRange(candidateErrors);
        questions = candidates.Select(question => new NewQuestionDefinition(
            question.Type, question.Title, question.Description, question.Required, question.SortOrder, question.Config,
            question.Options.Select(option => new NewOptionDefinition(option.Label, option.Value, option.SortOrder)).ToArray())).ToArray();
        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    private static bool TryCreateQuestion(
        JsonElement questionElement,
        string path,
        out QuestionDefinitionCandidate definition,
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

        var optionDefinitions = Array.Empty<OptionDefinitionCandidate>();
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

        if (!hasType || !hasTitle || !hasSortOrder || validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        definition = new QuestionDefinitionCandidate
        {
            Id = Guid.NewGuid(), Type = type, Title = title, Description = description,
            Required = required, SortOrder = sortOrder, Config = configElement.Clone()
        };
        definition.Options.AddRange(optionDefinitions);
        foreach (var property in properties.Keys) definition.Source.Set(property, $"{path}.{property}");
        errors = [];
        return true;
    }

    private static void TryCreateOptions(
        JsonElement optionsElement,
        string optionsPath,
        List<ApiErrorDetail> validationErrors,
        out OptionDefinitionCandidate[] options)
    {
        var definitions = new List<OptionDefinitionCandidate>();
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
                var hasSortOrder = TryReadSortOrder(properties, "sortOrder", $"{path}.sortOrder", itemErrors, out var sortOrder);
                if (hasLabel && hasValue && hasSortOrder && itemErrors.Count == 0)
                {
                    var option = new OptionDefinitionCandidate { Id = Guid.NewGuid(), Label = label, Value = value, SortOrder = sortOrder };
                    foreach (var property in properties.Keys) option.Source.Set(property, $"{path}.{property}");
                    definitions.Add(option);
                }
            }

            validationErrors.AddRange(itemErrors);
            index++;
        }

        options = [.. definitions];
    }

    public static bool TryNormalizeQuestions(
        IReadOnlyList<QuestionDefinitionCandidate> questions,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        var orders = new Dictionary<int, QuestionDefinitionCandidate>();
        foreach (var question in questions)
        {
            question.Title = TextRules.Trim(question.Title);
            if (question.Title.Length == 0)
                validationErrors.Add(new(question.Source.Path("title"), "REQUIRED_FIELD", "Title is required."));
            if (question.Description is { } description)
            {
                description = TextRules.Trim(description);
                question.Description = description.Length == 0 ? null : description;
            }
            if (TryNormalizeConfig(question.Type, question.Config, question.Source.Path("config"), validationErrors, out var config))
                question.Config = config;
            if (!orders.TryAdd(question.SortOrder, question))
                validationErrors.Add(new(question.Source.ConflictPath("sortOrder", orders[question.SortOrder].Source),
                    "DUPLICATE_SORT_ORDER", "Question sortOrder values must be unique within a form."));
            if (!IsChoice(question.Type) && question.Options.Count > 0)
                validationErrors.Add(new(question.Source.Path("options"), "INVALID_OPTION", "Only choice questions may contain options."));

            var optionOrders = new Dictionary<int, OptionDefinitionCandidate>();
            var values = new Dictionary<string, OptionDefinitionCandidate>(StringComparer.Ordinal);
            foreach (var option in question.Options)
            {
                option.Label = TextRules.Trim(option.Label);
                option.Value = TextRules.Trim(option.Value);
                if (option.Label.Length == 0)
                    validationErrors.Add(new(option.Source.Path("label"), "REQUIRED_FIELD", "Label is required."));
                if (option.Value.Length == 0)
                    validationErrors.Add(new(option.Source.Path("value"), "REQUIRED_FIELD", "Value is required."));
                else if (TextRules.CodePointCount(option.Value) > 255)
                    validationErrors.Add(new(option.Source.Path("value"), "INVALID_OPTION", "Option value must not exceed 255 characters."));
                if (!optionOrders.TryAdd(option.SortOrder, option))
                    validationErrors.Add(new(option.Source.ConflictPath("sortOrder", optionOrders[option.SortOrder].Source),
                        "DUPLICATE_SORT_ORDER", "Option sortOrder values must be unique within a question."));
                if (!values.TryAdd(option.Value, option))
                    validationErrors.Add(new(option.Source.ConflictPath("value", values[option.Value].Source),
                        "INVALID_OPTION", "Option values must be unique within a question."));
            }
        }
        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    private static bool TryNormalizeConfig(
        QuestionType type,
        JsonElement configElement,
        string path,
        List<ApiErrorDetail> errors,
        out JsonElement config)
    {
        config = EmptyObject;
        var initialErrorCount = errors.Count;
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
        if (errors.Count > initialErrorCount)
        {
            return false;
        }

        config = JsonSerializer.SerializeToElement(result);
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
            config["min"] is JsonValue numberMinValue &&
            config["max"] is JsonValue numberMaxValue &&
            numberMinValue.TryGetValue<JsonElement>(out var minElement) &&
            numberMaxValue.TryGetValue<JsonElement>(out var maxElement) &&
            DefinitionNumber.TryRead(minElement, out var numberMin) &&
            DefinitionNumber.TryRead(maxElement, out var numberMax) &&
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
