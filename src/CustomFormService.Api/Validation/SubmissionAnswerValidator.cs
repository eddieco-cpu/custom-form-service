using System.Text.Json;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Validation;

// No persistence, time sampling or lifecycle checks. The caller runs the envelope
// check before resource lookup, and answer validation only after the submit gates.
public static class SubmissionAnswerValidator
{
    private static readonly HashSet<string> EnvelopeProperties = new(["answers"], StringComparer.Ordinal);
    private static readonly HashSet<string> AnswerProperties = new(["questionId", "value"], StringComparer.Ordinal);
    private static readonly HashSet<string> RequiredAnswerProperties = new(["questionId", "value"], StringComparer.Ordinal);
    private static readonly HashSet<string> NoRequiredProperties = new(StringComparer.Ordinal);

    public static bool TryReadEnvelope(
        JsonElement body,
        out JsonElement answers,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        answers = default;
        if (!TryReadObject(body, EnvelopeProperties, NoRequiredProperties, "body", out var properties, out errors))
            return false;

        // Missing/non-array answers are intentionally deferred to API step 8.
        if (!properties.TryGetValue("answers", out var supplied)) return true;
        var shapeErrors = new List<ApiErrorDetail>();
        if (supplied.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in supplied.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    !TryReadObject(item, AnswerProperties, NoRequiredProperties, $"answers[{index}]", out _, out var itemErrors))
                    shapeErrors.AddRange(itemErrors);
                index++;
            }
        }
        errors = shapeErrors;
        if (shapeErrors.Count != 0) return false;
        answers = supplied.Clone();
        return true;
    }

    public static bool TryNormalizeAnswers(
        JsonElement answers,
        IReadOnlyList<SubmissionQuestionDefinition> questions,
        out IReadOnlyList<NormalizedSubmissionAnswer> normalized,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        normalized = [];
        if (!TryReadAnswers(answers, out var requests, out errors)) return false;

        var byId = questions.ToDictionary(question => question.Id);
        var validationErrors = new List<ApiErrorDetail>();
        foreach (var request in requests)
        {
            if (!byId.ContainsKey(request.QuestionId))
                validationErrors.Add(new($"answers[{request.Index}].questionId", "QUESTION_NOT_FOUND", "The question was not found in this form."));
        }
        // Question membership is its own early-return stage, before required/type/option rules.
        if (validationErrors.Count != 0)
        {
            errors = validationErrors;
            return false;
        }

        var suppliedIds = requests.Select(request => request.QuestionId).ToHashSet();
        foreach (var question in questions)
        {
            if (question.Required && !suppliedIds.Contains(question.Id))
                validationErrors.Add(Required($"questions.{question.Id:D}"));
        }

        var result = new List<NormalizedSubmissionAnswer>();
        foreach (var request in requests)
        {
            var question = byId[request.QuestionId];
            var path = $"answers[{request.Index}].value";
            var value = request.Value;
            switch (question.Type)
            {
                case QuestionType.Text:
                    if (!TryReadString(value, out var text) || text.Contains('\0'))
                    {
                        validationErrors.Add(RequestJson.Invalid(path, "The answer must be a valid text string without NUL."));
                        break;
                    }
                    text = TextRules.Trim(text);
                    if (text.Length == 0)
                    {
                        if (question.Required) validationErrors.Add(Required(path));
                        break;
                    }
                    var length = TextRules.CodePointCount(text);
                    if ((question.Config.TryGetProperty("minLength", out var minLength) && length < minLength.GetInt32()) ||
                        (question.Config.TryGetProperty("maxLength", out var maxLength) && length > maxLength.GetInt32()))
                        validationErrors.Add(RequestJson.Invalid(path, "The text length is outside the configured range."));
                    else
                        result.Add(new(question.Id, JsonSerializer.SerializeToElement(text)));
                    break;

                case QuestionType.Number:
                    if (!DefinitionNumber.TryRead(value, out var number))
                        validationErrors.Add(RequestJson.Invalid(path, "The answer must be a safely storable JSON number."));
                    else if ((question.Config.TryGetProperty("min", out var min) && DefinitionNumber.TryRead(min, out var lower) && number.CompareTo(lower) < 0) ||
                             (question.Config.TryGetProperty("max", out var max) && DefinitionNumber.TryRead(max, out var upper) && number.CompareTo(upper) > 0))
                        validationErrors.Add(RequestJson.Invalid(path, "The number is outside the configured range."));
                    else
                        result.Add(new(question.Id, value.Clone()));
                    break;

                case QuestionType.Rating:
                    // Same Integer predicate as the existing definition validator; no coercion.
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var rating) ||
                        rating < question.Config.GetProperty("min").GetInt32() || rating > question.Config.GetProperty("max").GetInt32())
                        validationErrors.Add(RequestJson.Invalid(path, "The answer must be an integer in the configured range."));
                    else
                        result.Add(new(question.Id, JsonSerializer.SerializeToElement(rating)));
                    break;

                case QuestionType.SingleChoice:
                    if (!TryReadUuid(value, out var optionId))
                        validationErrors.Add(RequestJson.Invalid(path, "The answer must be an option UUID string."));
                    else if (!question.OptionIds.Contains(optionId))
                        validationErrors.Add(OptionNotFound(path));
                    else
                        result.Add(new(question.Id, JsonSerializer.SerializeToElement(optionId.ToString("D"))));
                    break;

                case QuestionType.MultipleChoice:
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        validationErrors.Add(RequestJson.Invalid(path, "The answer must be an array of option UUID strings."));
                        break;
                    }
                    if (value.GetArrayLength() == 0)
                    {
                        if (question.Required) validationErrors.Add(Required(path));
                        break;
                    }
                    var ids = new List<string>();
                    var seen = new HashSet<Guid>();
                    var optionIndex = 0;
                    var errorsBefore = validationErrors.Count;
                    foreach (var option in value.EnumerateArray())
                    {
                        var optionPath = $"{path}[{optionIndex++}]";
                        if (!TryReadUuid(option, out var selectedId))
                            validationErrors.Add(RequestJson.Invalid(optionPath, "The option must be a UUID string."));
                        else if (!seen.Add(selectedId))
                            validationErrors.Add(RequestJson.Invalid(optionPath, "An option ID must not be repeated."));
                        else if (!question.OptionIds.Contains(selectedId))
                            validationErrors.Add(OptionNotFound(optionPath));
                        else
                            ids.Add(selectedId.ToString("D"));
                    }
                    if (validationErrors.Count == errorsBefore)
                        result.Add(new(question.Id, JsonSerializer.SerializeToElement(ids)));
                    break;

                default:
                    throw new InvalidOperationException("The published question type is unsupported.");
            }
        }

        errors = validationErrors;
        if (validationErrors.Count != 0) return false;
        normalized = result;
        return true;
    }

    private static bool TryReadAnswers(JsonElement answers, out List<AnswerRequest> requests, out IReadOnlyList<ApiErrorDetail> errors)
    {
        requests = [];
        if (answers.ValueKind == JsonValueKind.Undefined)
        {
            errors = [Required("answers")];
            return false;
        }
        if (answers.ValueKind != JsonValueKind.Array)
        {
            errors = [RequestJson.Invalid("answers", "Answers must be an array.")];
            return false;
        }

        var shapeErrors = new List<ApiErrorDetail>();
        var seen = new HashSet<Guid>();
        var index = 0;
        foreach (var item in answers.EnumerateArray())
        {
            var itemPath = $"answers[{index}]";
            if (!TryReadObject(item, AnswerProperties, RequiredAnswerProperties, itemPath, out var properties, out var itemErrors))
                shapeErrors.AddRange(itemErrors);
            else if (!TryReadUuid(properties["questionId"], out var questionId))
                shapeErrors.Add(RequestJson.Invalid($"{itemPath}.questionId", "The question ID must be a UUID string."));
            else if (!seen.Add(questionId))
                shapeErrors.Add(RequestJson.Invalid($"{itemPath}.questionId", "A question ID must not be repeated."));
            else
                requests.Add(new(index, questionId, properties["value"]));
            index++;
        }
        errors = shapeErrors;
        return shapeErrors.Count == 0;
    }

    private static bool TryReadObject(
        JsonElement element, IReadOnlySet<string> allowed, IReadOnlySet<string> required, string path,
        out Dictionary<string, JsonElement> properties, out IReadOnlyList<ApiErrorDetail> errors)
    {
        try
        {
            var valid = RequestJson.TryReadObject(element, allowed, required, out properties, out var localErrors);
            errors = localErrors.Select(error => error with
            {
                Path = path == "body" ? error.Path : error.Path == "body" ? path : $"{path}.{error.Path}"
            }).ToArray();
            return valid;
        }
        catch (InvalidOperationException)
        {
            // Escaped lone surrogates in property names fail during string decoding.
            properties = [];
            errors = [RequestJson.Invalid(path, "The object contains an invalid JSON property name.")];
            return false;
        }
    }

    private static bool TryReadString(JsonElement value, out string text)
    {
        text = "";
        if (value.ValueKind != JsonValueKind.String) return false;
        try
        {
            text = value.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            // JsonDocument.Parse can accept an escape which GetString cannot decode.
            return false;
        }
    }

    private static bool TryReadUuid(JsonElement value, out Guid id)
    {
        id = default;
        return TryReadString(value, out var text) && Guid.TryParse(text, out id);
    }

    private static ApiErrorDetail Required(string path) => new(path, "REQUIRED_FIELD", "This field or answer is required.");
    private static ApiErrorDetail OptionNotFound(string path) => new(path, "OPTION_NOT_FOUND", "The option was not found in this question.");
    private sealed record AnswerRequest(int Index, Guid QuestionId, JsonElement Value);
}

// Definitions must already be valid/frozen. They are read, never mutated by this validator.
public sealed record SubmissionQuestionDefinition(Guid Id, QuestionType Type, bool Required, JsonElement Config, IReadOnlySet<Guid> OptionIds);
public sealed record NormalizedSubmissionAnswer(Guid QuestionId, JsonElement Value);
