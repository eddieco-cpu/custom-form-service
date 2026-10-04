using System.Net;
using System.Text.Json;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class SubmissionAnswerValidatorTests
{
    [TestMethod]
    public void Five_types_normalize_without_mutating_definitions_or_request()
    {
        var text = Question(QuestionType.Text, true, "{\"minLength\":2,\"maxLength\":8}");
        var number = Question(QuestionType.Number, true, "{\"min\":1,\"max\":2}");
        var rating = Question(QuestionType.Rating, true, "{\"min\":0,\"max\":10}");
        var single = Question(QuestionType.SingleChoice, true);
        var multiple = Question(QuestionType.MultipleChoice, true);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        single.OptionIds.Add(first);
        multiple.OptionIds.UnionWith([first, second]);
        var questions = new[] { text.Definition, number.Definition, rating.Definition, single.Definition, multiple.Definition };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            answers = new object[]
            {
                new { questionId = multiple.Definition.Id, value = new[] { second.ToString("D").ToUpperInvariant(), first.ToString("D") } },
                new { questionId = text.Definition.Id, value = "\u2003 A\U0001F600 \u00A0" },
                new { questionId = number.Definition.Id, value = Json("1.00000000000000000000000000001") },
                new { questionId = rating.Definition.Id, value = 0 },
                new { questionId = single.Definition.Id, value = first.ToString("D").ToUpperInvariant() }
            }
        }));
        var raw = document.RootElement.GetRawText();
        var configs = questions.Select(question => question.Config.GetRawText()).ToArray();
        Assert.IsTrue(SubmissionAnswerValidator.TryReadEnvelope(document.RootElement, out var answers, out var envelopeErrors), Describe(envelopeErrors));
        Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(answers, questions, out var result, out var errors), Describe(errors));
        Assert.HasCount(5, result);
        Assert.AreEqual("A\U0001F600", result[1].Value.GetString());
        Assert.AreEqual("1.00000000000000000000000000001", result[2].Value.GetRawText());
        Assert.AreEqual(0, result[3].Value.GetInt32());
        Assert.AreEqual(first.ToString("D"), result[4].Value.GetString());
        CollectionAssert.AreEqual(new[] { second.ToString("D"), first.ToString("D") }, result[0].Value.EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.AreEqual(raw, document.RootElement.GetRawText());
        CollectionAssert.AreEqual(configs, questions.Select(question => question.Config.GetRawText()).ToArray());
        CollectionAssert.AreEquivalent(new[] { first, second }, multiple.OptionIds.ToArray());
    }

    [TestMethod]
    [DataRow("{}", "REQUIRED_FIELD")]
    [DataRow("{\"answers\":null}", "INVALID_VALUE")]
    [DataRow("{\"answers\":{}}", "INVALID_VALUE")]
    [DataRow("{\"answers\":false}", "INVALID_VALUE")]
    public void Envelope_defers_required_and_array_checks_until_answer_stage(string body, string code)
    {
        using var document = JsonDocument.Parse(body);
        Assert.IsTrue(SubmissionAnswerValidator.TryReadEnvelope(document.RootElement, out var answers, out var envelopeErrors), Describe(envelopeErrors));
        Assert.IsEmpty(envelopeErrors, "The pre-resource stage must not reject missing/non-array answers. Non-Published is checked by B before answer validation.");
        Assert.IsFalse(SubmissionAnswerValidator.TryNormalizeAnswers(answers, [], out var result, out var errors));
        Assert.IsEmpty(result);
        AssertDetails(errors, ("answers", code));
    }

    [TestMethod]
    [DataRow("null", "body")]
    [DataRow("[]", "body")]
    [DataRow("{\"userId\":\"client\"}", "userId")]
    [DataRow("{\"answers\":[],\"answers\":[]}", "answers")]
    [DataRow("{\"answers\":[{\"responseId\":\"client\"}]}", "answers[0].responseId")]
    [DataRow("{\"answers\":[{\"value\":1,\"value\":2}]}", "answers[0].value")]
    [DataRow("{\"\\uD800\":1}", "body")]
    [DataRow("{\"answers\":[{\"\\uDC00\":1}]}", "answers[0]")]
    public void Envelope_rejects_unknown_readonly_duplicate_or_undecodable_fields(string body, string path)
    {
        using var document = JsonDocument.Parse(body);
        Assert.IsFalse(SubmissionAnswerValidator.TryReadEnvelope(document.RootElement, out _, out var errors));
        AssertDetails(errors, (path, "INVALID_VALUE"));
        AssertFormal400(errors);
    }

    [TestMethod]
    public void Item_shape_and_duplicate_ids_return_before_question_membership()
    {
        var unknownId = Guid.NewGuid();
        using var document = JsonDocument.Parse($$"""
            [{"questionId":"{{unknownId}}","value":1},
             {"questionId":"{{unknownId.ToString("D").ToUpperInvariant()}}","value":null},
             {"questionId":null,"value":1},
             {"questionId":"{{Guid.NewGuid()}}"},
             false]
            """);
        Assert.IsFalse(SubmissionAnswerValidator.TryNormalizeAnswers(document.RootElement, [], out var result, out var errors));
        Assert.IsEmpty(result);
        AssertDetails(errors, ("answers[1].questionId", "INVALID_VALUE"), ("answers[2].questionId", "INVALID_VALUE"),
            ("answers[3].value", "REQUIRED_FIELD"), ("answers[4]", "INVALID_VALUE"));
    }

    [TestMethod]
    public void Missing_question_id_is_a_shape_required_field()
    {
        AssertInvalid(Json("[{\"value\":1}]"), [], ("answers[0].questionId", "REQUIRED_FIELD"));
    }

    [TestMethod]
    public void Question_membership_errors_precede_required_and_invalid_values()
    {
        var required = Question(QuestionType.Text, true);
        var otherFormQuestion = Question(QuestionType.Number);
        var unknownQuestion = Guid.NewGuid();
        var answers = Json($$"""[{"questionId":"{{otherFormQuestion.Definition.Id}}","value":null},{"questionId":"{{unknownQuestion}}","value":{} }]""");
        AssertInvalid(answers, [required.Definition], ("answers[0].questionId", "QUESTION_NOT_FOUND"), ("answers[1].questionId", "QUESTION_NOT_FOUND"));
    }

    [TestMethod]
    public void Required_missing_question_uses_question_id_path()
    {
        var question = Question(QuestionType.Text, true);
        AssertInvalid(Json("[]"), [question.Definition], ($"questions.{question.Definition.Id:D}", "REQUIRED_FIELD"));
    }

    [TestMethod]
    [DataRow(QuestionType.Text, "\" \\u00A0\\u2003 \"", false)]
    [DataRow(QuestionType.Text, "\"\"", false)]
    [DataRow(QuestionType.MultipleChoice, "[]", false)]
    [DataRow(QuestionType.Text, "\" \\u00A0\\u2003 \"", true)]
    [DataRow(QuestionType.Text, "\"\"", true)]
    [DataRow(QuestionType.MultipleChoice, "[]", true)]
    public void Legal_empty_answers_are_omitted_or_required(QuestionType type, string token, bool required)
    {
        var question = Question(type, required, type == QuestionType.Text ? "{\"minLength\":5}" : "{}");
        var answers = Answer(question.Definition.Id, token);
        if (required) AssertInvalid(answers, [question.Definition], ("answers[0].value", "REQUIRED_FIELD"));
        else
        {
            Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(answers, [question.Definition], out var result, out var errors), Describe(errors));
            Assert.IsEmpty(result);
        }
    }

    [TestMethod]
    public void All_optional_questions_allow_an_empty_answer_collection()
    {
        var definitions = new[] { Question(QuestionType.Text, config: "{\"minLength\":5}").Definition, Question(QuestionType.Number).Definition };
        Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(Json("[]"), definitions, out var result, out var errors), Describe(errors));
        Assert.IsEmpty(result);
    }

    [TestMethod]
    [DataRow(QuestionType.Text, false)]
    [DataRow(QuestionType.Text, true)]
    [DataRow(QuestionType.Number, false)]
    [DataRow(QuestionType.Number, true)]
    [DataRow(QuestionType.Rating, false)]
    [DataRow(QuestionType.Rating, true)]
    [DataRow(QuestionType.SingleChoice, false)]
    [DataRow(QuestionType.SingleChoice, true)]
    [DataRow(QuestionType.MultipleChoice, false)]
    [DataRow(QuestionType.MultipleChoice, true)]
    public void Explicit_null_is_invalid_for_required_and_optional_questions(QuestionType type, bool required)
    {
        var question = Question(type, required);
        AssertInvalid(Answer(question.Definition.Id, "null"), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
    }

    [TestMethod]
    [DataRow(QuestionType.Text, "42")]
    [DataRow(QuestionType.Number, "\"1\"")]
    [DataRow(QuestionType.Number, "true")]
    [DataRow(QuestionType.Rating, "\"5\"")]
    [DataRow(QuestionType.SingleChoice, "[]")]
    [DataRow(QuestionType.MultipleChoice, "\"label\"")]
    public void Incorrect_value_shapes_are_invalid_even_when_optional(QuestionType type, string token)
    {
        var question = Question(type);
        AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
    }

    [TestMethod]
    [DataRow("5", true)]
    [DataRow("5.0", false)]
    [DataRow("5e0", false)]
    [DataRow("2147483648", false)]
    [DataRow("1e1000", false)]
    [DataRow("0", true)]
    [DataRow("10", true)]
    [DataRow("-1", false)]
    [DataRow("11", false)]
    public void Rating_uses_existing_integer_predicate_and_inclusive_bounds(string token, bool valid)
    {
        var question = Question(QuestionType.Rating);
        AssertValidOrInvalid(Answer(question.Definition.Id, token), question.Definition, valid);
    }

    [TestMethod]
    [DataRow("\"a\\u0000b\"")]
    [DataRow("\"\\u0000\"")]
    [DataRow("\"\\uD800\"")]
    [DataRow("\"\\uDC00\"")]
    [DataRow("\"\\uD800x\"")]
    public void Text_decode_and_jsonb_incompatible_characters_return_located_formal_400(string token)
    {
        var question = Question(QuestionType.Text, true);
        AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
    }

    [TestMethod]
    [DataRow("\"\\uD83D\\uDE00\"", "\U0001F600", true)]
    [DataRow("\" e\\u0301 \"", "e\u0301", false)]
    [DataRow("\" é \"", "é", true)]
    [DataRow("\" \\u200B \"", "\u200B", true)]
    public void Text_length_counts_code_points_without_replacement_or_normalization(string token, string expected, bool valid)
    {
        var question = Question(QuestionType.Text, config: "{\"minLength\":1,\"maxLength\":1}");
        if (!valid) AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
        else
        {
            Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(Answer(question.Definition.Id, token), [question.Definition], out var result, out var errors), Describe(errors));
            Assert.AreEqual(expected, result.Single().Value.GetString());
        }
    }

    [TestMethod]
    [DataRow("1.00000000000000000000000000001", true)]
    [DataRow("1.00000000000000000000000000002", true)]
    [DataRow("1.00000000000000000000000000000", false)]
    [DataRow("1.00000000000000000000000000003", false)]
    public void Number_range_comparison_does_not_round_through_decimal_or_double(string token, bool valid)
    {
        var question = Question(QuestionType.Number, config: "{\"min\":1.00000000000000000000000000001,\"max\":1.00000000000000000000000000002}");
        AssertValidOrInvalid(Answer(question.Definition.Id, token), question.Definition, valid);
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("1.0", true)]
    [DataRow("1e0", true)]
    [DataRow("1.00000000000000000000000000001", false)]
    [DataRow("0.99999999999999999999999999999", false)]
    public void Numerically_equal_tokens_share_the_same_number_bounds(string token, bool valid)
    {
        var question = Question(QuestionType.Number, config: "{\"min\":1,\"max\":1}");
        AssertValidOrInvalid(Answer(question.Definition.Id, token), question.Definition, valid);
    }

    [TestMethod]
    [DataRow("1e131072")]
    [DataRow("1e-16384")]
    [DataRow("0e2147483647")]
    [DataRow("0e-2147483647")]
    public void Numbers_outside_existing_jsonb_safe_storage_are_controlled_errors(string token)
    {
        var question = Question(QuestionType.Number);
        AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
    }

    [TestMethod]
    [DataRow(QuestionType.SingleChoice)]
    [DataRow(QuestionType.MultipleChoice)]
    public void Valid_uuid_options_from_other_questions_and_nonexistent_options_are_not_found(QuestionType type)
    {
        var question = Question(type);
        var other = Question(type);
        var foreignId = Guid.NewGuid();
        other.OptionIds.Add(foreignId);
        var unknownId = Guid.NewGuid();
        if (type == QuestionType.SingleChoice)
            AssertInvalid(Answer(question.Definition.Id, JsonSerializer.Serialize(foreignId)), [question.Definition], ("answers[0].value", "OPTION_NOT_FOUND"));
        else
            AssertInvalid(Answer(question.Definition.Id, JsonSerializer.Serialize(new[] { foreignId, unknownId })), [question.Definition],
                ("answers[0].value[0]", "OPTION_NOT_FOUND"), ("answers[0].value[1]", "OPTION_NOT_FOUND"));
    }

    [TestMethod]
    public void Multiple_choice_duplicate_and_malformed_elements_keep_original_indexes()
    {
        var question = Question(QuestionType.MultipleChoice);
        var id = Guid.NewGuid();
        question.OptionIds.Add(id);
        var token = $"[\"{id:D}\",\"{id.ToString("D").ToUpperInvariant()}\",null,\"label\",\"\\uD800\"]";
        AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value[1]", "INVALID_VALUE"),
            ("answers[0].value[2]", "INVALID_VALUE"), ("answers[0].value[3]", "INVALID_VALUE"), ("answers[0].value[4]", "INVALID_VALUE"));
    }

    [TestMethod]
    public void Same_answer_stage_collects_safe_errors_and_never_returns_partial_normalized_results()
    {
        var missing = Question(QuestionType.Text, true);
        var wrong = Question(QuestionType.Number);
        var choice = Question(QuestionType.SingleChoice);
        var valid = Question(QuestionType.Text);
        var answers = Json($$"""[{"questionId":"{{valid.Definition.Id}}","value":"ok"},{"questionId":"{{wrong.Definition.Id}}","value":null},{"questionId":"{{choice.Definition.Id}}","value":"{{Guid.NewGuid()}}"}]""");
        AssertInvalid(answers, [missing.Definition, wrong.Definition, choice.Definition, valid.Definition],
            ($"questions.{missing.Definition.Id:D}", "REQUIRED_FIELD"), ("answers[1].value", "INVALID_VALUE"), ("answers[2].value", "OPTION_NOT_FOUND"));
    }

    [TestMethod]
    public void Envelope_and_all_normalized_values_outlive_source_json_documents()
    {
        var text = Question(QuestionType.Text);
        var number = Question(QuestionType.Number);
        var rating = Question(QuestionType.Rating);
        var single = Question(QuestionType.SingleChoice);
        var multiple = Question(QuestionType.MultipleChoice);
        var option = Guid.NewGuid();
        single.OptionIds.Add(option);
        multiple.OptionIds.Add(option);
        JsonElement ownedAnswers;
        using (var document = JsonDocument.Parse($$"""
            {"answers":[{"questionId":"{{text.Definition.Id}}","value":" original "},
                        {"questionId":"{{number.Definition.Id}}","value":1.00000000000000000000000000001},
                        {"questionId":"{{rating.Definition.Id}}","value":5},
                        {"questionId":"{{single.Definition.Id}}","value":"{{option}}"},
                        {"questionId":"{{multiple.Definition.Id}}","value":["{{option}}"]}]}
            """))
            Assert.IsTrue(SubmissionAnswerValidator.TryReadEnvelope(document.RootElement, out ownedAnswers, out var errors), Describe(errors));

        IReadOnlyList<NormalizedSubmissionAnswer> result;
        using (var anotherDocument = JsonDocument.Parse(ownedAnswers.GetRawText()))
            Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(anotherDocument.RootElement,
                [text.Definition, number.Definition, rating.Definition, single.Definition, multiple.Definition], out result, out var errors), Describe(errors));
        Assert.AreEqual("original", result[0].Value.GetString());
        Assert.AreEqual("1.00000000000000000000000000001", result[1].Value.GetRawText());
        Assert.AreEqual(5, result[2].Value.GetInt32());
        Assert.AreEqual(option.ToString("D"), result[3].Value.GetString());
        Assert.AreEqual(option.ToString("D"), result[4].Value[0].GetString());
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.AreEqual(5, serialized.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task Accepted_number_tokens_round_trip_as_exact_jsonb_and_rejected_tokens_fail_storage()
    {
        await using var factory = new CustomFormServiceApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
        await using var connection = new NpgsqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();
        var question = Question(QuestionType.Number);
        foreach (var token in new[] { "1", "1.0", "1e0", "-0", "1.00000000000000000000000000001", "1e131071", "1e-16383" })
        {
            Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(Answer(question.Definition.Id, token), [question.Definition], out var result, out var errors), Describe(errors));
            await using var command = new NpgsqlCommand("SELECT @value::jsonb = @original::jsonb;", connection);
            command.Parameters.AddWithValue("value", result.Single().Value.GetRawText());
            command.Parameters.AddWithValue("original", token);
            Assert.IsTrue((bool)(await command.ExecuteScalarAsync())!, token);
        }
        foreach (var token in new[] { "1e131072", "1e-16384", "0e2147483647", "0e-2147483647" })
        {
            AssertInvalid(Answer(question.Definition.Id, token), [question.Definition], ("answers[0].value", "INVALID_VALUE"));
            await using var command = new NpgsqlCommand("SELECT @value::jsonb;", connection);
            command.Parameters.AddWithValue("value", token);
            await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
        }
        // Different spelling is deliberately not a receipt promise; DB numeric equality is.
        await using var equality = new NpgsqlCommand("SELECT '1'::jsonb = '1.0'::jsonb AND '1.0'::jsonb = '1e0'::jsonb;", connection);
        Assert.IsTrue((bool)(await equality.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task Text_jsonb_probe_rejects_nul_and_accepts_valid_pair_without_replacement()
    {
        await using var factory = new CustomFormServiceApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
        await using var connection = new NpgsqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();
        await using var invalid = new NpgsqlCommand("SELECT @value::jsonb;", connection);
        invalid.Parameters.AddWithValue("value", "\"a\\u0000b\"");
        var failure = await Assert.ThrowsAsync<PostgresException>(async () => await invalid.ExecuteScalarAsync());
        Assert.AreEqual("22P05", failure.SqlState);
        var question = Question(QuestionType.Text);
        Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(Answer(question.Definition.Id, "\"\\uD83D\\uDE00\""), [question.Definition], out var result, out var errors), Describe(errors));
        await using var valid = new NpgsqlCommand("SELECT @value::jsonb #>> '{}';", connection);
        valid.Parameters.AddWithValue("value", result.Single().Value.GetRawText());
        Assert.AreEqual("\U0001F600", await valid.ExecuteScalarAsync());
    }

    private static (SubmissionQuestionDefinition Definition, HashSet<Guid> OptionIds) Question(QuestionType type, bool required = false, string? config = null)
    {
        var options = new HashSet<Guid>();
        return (new(Guid.NewGuid(), type, required, Json(config ?? (type == QuestionType.Rating ? "{\"min\":0,\"max\":10}" : "{}")), options), options);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement Answer(Guid questionId, string value) => Json($$"""[{"questionId":"{{questionId}}","value":{{value}}}]""");
    private static string Describe(IReadOnlyList<ApiErrorDetail> errors) => string.Join(", ", errors.Select(error => $"{error.Path}: {error.Code}"));

    private static void AssertDetails(IReadOnlyList<ApiErrorDetail> actual, params (string Path, string Code)[] expected) =>
        CollectionAssert.AreEqual(expected, actual.Select(error => (error.Path, error.Code)).ToArray(), Describe(actual));

    private static void AssertInvalid(JsonElement answers, IReadOnlyList<SubmissionQuestionDefinition> definitions, params (string Path, string Code)[] expected)
    {
        Assert.IsFalse(SubmissionAnswerValidator.TryNormalizeAnswers(answers, definitions, out var result, out var errors));
        Assert.IsEmpty(result);
        AssertDetails(errors, expected);
        AssertFormal400(errors);
    }

    private static void AssertFormal400(IReadOnlyList<ApiErrorDetail> errors)
    {
        var response = ApiErrors.Validation(errors);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsInstanceOfType<ApiErrorResponse>(response.Value);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var error = document.RootElement.GetProperty("error");
        Assert.AreEqual("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.AreEqual(errors.Count, error.GetProperty("details").GetArrayLength());
    }

    private static void AssertValidOrInvalid(JsonElement answers, SubmissionQuestionDefinition definition, bool expectedValid)
    {
        if (!expectedValid) AssertInvalid(answers, [definition], ("answers[0].value", "INVALID_VALUE"));
        else
        {
            Assert.IsTrue(SubmissionAnswerValidator.TryNormalizeAnswers(answers, [definition], out var result, out var errors), Describe(errors));
            Assert.HasCount(1, result);
        }
    }
}
