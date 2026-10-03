using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    public async Task Patch_empty_question_and_option_arrays_are_persisted_no_ops()
    {
        var owner = await CreateMemberAsync("R6 Empty Delta Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var questionId = before.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var rows = await ReadDefinitionRowsAsync(formId);
        foreach (var body in new object[]
        {
            new { questions = Array.Empty<object>() },
            new { questions = new[] { new { id = questionId, options = Array.Empty<object>() } } }
        })
        {
            var response = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(body));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            using var actual = await ReadJsonAsync(response);
            Assert.IsTrue(JsonElement.DeepEquals(before.RootElement, actual.RootElement));
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId), "Empty delta must retain every row, ID and timestamp.");
        }
    }

    [TestMethod]
    [DataRow("text", "single_choice")]
    [DataRow("number", "multiple_choice")]
    [DataRow("single_choice", "number")]
    [DataRow("multiple_choice", "rating")]
    [DataRow("number", "text")]
    public async Task Patch_type_changes_reset_config_and_apply_target_option_rules(string from, string to)
    {
        var owner = await CreateMemberAsync("R6 Type Matrix Owner");
        var choiceFrom = from.EndsWith("choice", StringComparison.Ordinal);
        var choiceTo = to.EndsWith("choice", StringComparison.Ordinal);
        var config = ParseJson(from == "text" ? "{\"placeholder\":\"old\"}" : from == "number" ? "{\"min\":-2,\"max\":2}" : "{\"variant\":\"select\"}");
        // multiple_choice uses its own variant vocabulary.
        if (from == "multiple_choice") config = ParseJson("{\"variant\":\"multi_select\"}");
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie,
            JsonContent.Create(new { title = "Type matrix", questions = new[] { new { type = from, title = "Q", sortOrder = 0, config, options = choiceFrom ? new[] { new { label = "Old", value = "old", sortOrder = 0 } } : [] } } }));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        using var created = await ReadJsonAsync(create);
        var formId = created.RootElement.GetProperty("id").GetGuid();
        var id = created.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var before = await ScalarAsync<DateTime>("SELECT created_at FROM questions WHERE id = @id;", new NpgsqlParameter("id", id));
        object delta = choiceTo
            ? new { id, type = to, options = new[] { new { label = "New", value = " new ", sortOrder = 0 } } }
            : new { id, type = to, options = Array.Empty<object>() };
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(new { questions = new[] { delta } }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var actual = await GetFormJsonAsync(formId, owner.Cookie);
        var question = actual.RootElement.GetProperty("questions")[0];
        Assert.AreEqual(to, question.GetProperty("type").GetString());
        Assert.AreEqual(0, question.GetProperty("config").EnumerateObject().Count());
        Assert.AreEqual(choiceTo ? 1 : 0, question.GetProperty("options").GetArrayLength());
        if (choiceTo) Assert.AreEqual("new", question.GetProperty("options")[0].GetProperty("value").GetString());
        Assert.AreEqual(before, await ScalarAsync<DateTime>("SELECT created_at FROM questions WHERE id = @id;", new NpgsqlParameter("id", id)));
        using var rows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        AssertApiDefinitionMatchesRows(actual.RootElement, rows.RootElement);
    }

    [TestMethod]
    public async Task Patch_choice_interchange_preserves_options_and_uses_explicit_target_config()
    {
        var owner = await CreateMemberAsync("R6 Choice Switch Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var original = before.RootElement.GetProperty("questions")[0];
        var id = original.GetProperty("id").GetGuid();
        var options = original.GetProperty("options").GetRawText();
        foreach (var delta in new object[]
        {
            new { id, type = "multiple_choice", config = new { variant = "multi_select" } },
            new { id, type = "single_choice" }
        })
        {
            var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(new { questions = new[] { delta } }));
            Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
            using var actual = await GetFormJsonAsync(formId, owner.Cookie);
            var question = actual.RootElement.GetProperty("questions")[0];
            Assert.AreEqual(options, question.GetProperty("options").GetRawText());
            if (question.GetProperty("type").GetString() == "multiple_choice") Assert.AreEqual("multi_select", question.GetProperty("config").GetProperty("variant").GetString());
            else Assert.AreEqual(0, question.GetProperty("config").EnumerateObject().Count());
        }
    }

    [TestMethod]
    public async Task Patch_config_is_a_complete_replacement_and_semantic_equality_is_a_no_op()
    {
        var owner = await CreateMemberAsync("R6 Config Replacement Owner");
        var formId = await CreateConfiguredQuestionAsync(owner.Cookie, "text", ParseJson("{\"variant\":\"textarea\",\"placeholder\":\"old\",\"minLength\":1,\"maxLength\":5}"));
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var id = before.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id, config = new { maxLength = 10 } } } }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var actual = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.IsTrue(JsonElement.DeepEquals(ParseJson("{\"maxLength\":10}"), actual.RootElement.GetProperty("questions")[0].GetProperty("config")));

        var numberFormId = await CreateConfiguredQuestionAsync(owner.Cookie, "number", ParseJson("{\"min\":1,\"max\":2,\"placeholder\":\"hello\"}"));
        using var number = await GetFormJsonAsync(numberFormId, owner.Cookie);
        id = number.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var rows = await ReadDefinitionRowsAsync(numberFormId);
        var noop = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{numberFormId}", owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id, config = ParseJson("{\"placeholder\":\" hello \",\"min\":1.0,\"max\":2.0}") } } }));
        Assert.AreEqual(HttpStatusCode.OK, noop.StatusCode, await noop.Content.ReadAsStringAsync());
        Assert.AreEqual(rows, await ReadDefinitionRowsAsync(numberFormId), "Property order and equivalent numeric spelling are not actual definition changes.");
    }

    [TestMethod]
    [DataRow("rating", "{}", true)]
    [DataRow("rating", "{\"min\":0}", true)]
    [DataRow("rating", "{\"max\":100}", true)]
    [DataRow("rating", "{\"min\":0,\"max\":100}", true)]
    [DataRow("rating", "{\"min\":-1}", false)]
    [DataRow("rating", "{\"max\":101}", false)]
    [DataRow("rating", "{\"min\":1.5}", false)]
    [DataRow("rating", "{\"min\":5,\"max\":5}", false)]
    [DataRow("text", "{\"minLength\":0,\"maxLength\":0}", true)]
    [DataRow("text", "{\"minLength\":2,\"maxLength\":1}", false)]
    [DataRow("number", "{\"min\":null}", false)]
    [DataRow("multiple_choice", "{\"variant\":\"select\"}", false)]
    public async Task Draft_config_boundaries_are_shared_by_create_and_patch(string type, string json, bool valid)
    {
        var owner = await CreateMemberAsync("R6 Config Boundary Owner");
        var formId = await CreateConfiguredQuestionAsync(owner.Cookie, type, ParseJson("{}"));
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var id = before.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var rows = await ReadDefinitionRowsAsync(formId);
        var config = ParseJson(json);
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(new { questions = new[] { new { id, config } } }));
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie,
            JsonContent.Create(new { title = "Boundary create", questions = new[] { new { type, title = "Q", sortOrder = 0, config } } }));
        if (valid)
        {
            Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
            using var actual = await GetFormJsonAsync(formId, owner.Cookie);
            using var persisted = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
            AssertApiDefinitionMatchesRows(actual.RootElement, persisted.RootElement);
        }
        else
        {
            await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            await AssertErrorAsync(create, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId));
        }
    }

    [TestMethod]
    [DataRow("1e30", "1e31", true)]
    [DataRow("1e400", "1e401", true)]
    [DataRow("-1e401", "-1e400", true)]
    [DataRow("1e-40", "2e-40", true)]
    [DataRow("1.00000000000000000000000000002", "1.00000000000000000000000000001", false)]
    [DataRow("1e-40", "0", false)]
    [DataRow("0", "-0", true)]
    [DataRow("1.0", "1e0", true)]
    [DataRow("-2", "-1", true)]
    [DataRow("-1", "-2", false)]
    [DataRow("-1e-40", "-2e-40", false)]
    public async Task Number_config_compares_and_persists_exact_json_numbers(string min, string max, bool valid)
    {
        var owner = await CreateMemberAsync("R6 Exact Number Owner");
        var config = ParseJson($"{{\"min\":{min},\"max\":{max}}}");
        Assert.IsTrue(await ScalarAsync<bool>("SELECT @json::jsonb = @json::jsonb;", new NpgsqlParameter("json", config.GetRawText())), "Confirm these literals can be safely represented by PostgreSQL JSONB.");
        var formId = await CreateConfiguredQuestionAsync(owner.Cookie, "number", ParseJson("{}"));
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var id = before.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var rows = await ReadDefinitionRowsAsync(formId);
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(new { questions = new[] { new { id, config } } }));
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie,
            JsonContent.Create(new { title = "Exact number create", questions = new[] { new { type = "number", title = "Q", sortOrder = 0, config } } }));
        if (valid)
        {
            Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
            using var created = await ReadJsonAsync(create);
            foreach (var currentFormId in new[] { formId, created.RootElement.GetProperty("id").GetGuid() })
            {
                Assert.IsTrue(await ScalarAsync<bool>("SELECT config = @config::jsonb FROM questions WHERE form_id = @id;", new NpgsqlParameter("config", config.GetRawText()), new NpgsqlParameter("id", currentFormId)), "Persisted bounds must retain exact numeric value, without double/decimal rounding.");
                using var actual = await GetFormJsonAsync(currentFormId, owner.Cookie);
                Assert.IsTrue(JsonElement.DeepEquals(config, actual.RootElement.GetProperty("questions")[0].GetProperty("config")));
            }
        }
        else
        {
            await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            await AssertErrorAsync(create, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId));
        }
    }

    [TestMethod]
    [DataRow("1e131072")]
    [DataRow("1e-16384")]
    public async Task Number_config_rejects_values_outside_actual_jsonb_storage_without_writes(string number)
    {
        var owner = await CreateMemberAsync("R6 JSONB Number Boundary Owner");
        var config = ParseJson($"{{\"min\":{number}}}");
        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ScalarAsync<bool>("SELECT @json::jsonb = @json::jsonb;", new NpgsqlParameter("json", config.GetRawText())));
        Assert.AreEqual(PostgresErrorCodes.NumericValueOutOfRange, exception.SqlState);
        var formId = await CreateConfiguredQuestionAsync(owner.Cookie, "number", ParseJson("{}"));
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var id = before.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var rows = await ReadDefinitionRowsAsync(formId);
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id, config } } }));
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", owner.Cookie,
            JsonContent.Create(new { title = "Unsafe number", questions = new[] { new { type = "number", title = "Q", sortOrder = 0, config } } }));
        await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        await AssertErrorAsync(create, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId));
    }

    [TestMethod]
    public async Task Patch_sort_order_swaps_validate_only_the_final_candidate()
    {
        var owner = await CreateMemberAsync("R6 Sort Swap Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var a = before.RootElement.GetProperty("questions")[0];
        var b = before.RootElement.GetProperty("questions")[1];
        var aId = a.GetProperty("id").GetGuid();
        var bId = b.GetProperty("id").GetGuid();
        var options = a.GetProperty("options");
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { questions = new object[]
            {
                new { id = aId, sortOrder = 1, options = new[] { new { id = options[0].GetProperty("id").GetGuid(), sortOrder = 1 }, new { id = options[1].GetProperty("id").GetGuid(), sortOrder = 0 } } },
                new { id = bId, sortOrder = 0 }
            } }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var actual = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.AreEqual(bId, actual.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid());
        Assert.AreEqual(aId, actual.RootElement.GetProperty("questions")[1].GetProperty("id").GetGuid());
        Assert.AreEqual(options[1].GetProperty("id").GetGuid(), actual.RootElement.GetProperty("questions")[1].GetProperty("options")[0].GetProperty("id").GetGuid());
        using var rows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        AssertApiDefinitionMatchesRows(actual.RootElement, rows.RootElement);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Candidate_collision_path_uses_the_request_source_of_the_relevant_field(bool optionCollision)
    {
        var owner = await CreateMemberAsync("R6 Field Source Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var questions = before.RootElement.GetProperty("questions");
        var a = questions[0];
        var rows = await ReadDefinitionRowsAsync(formId);
        // Both directions ensure collection traversal order cannot accidentally mask
        // a touched item's unrelated title/label being mistaken for sortOrder input.
        for (var direction = 0; direction < 2; direction++)
        {
            var touched = optionCollision ? a.GetProperty("options")[direction] : questions[direction];
            var changed = optionCollision ? a.GetProperty("options")[1 - direction] : questions[1 - direction];
            var touchedId = touched.GetProperty("id").GetGuid();
            var changedId = changed.GetProperty("id").GetGuid();
            var sortOrder = touched.GetProperty("sortOrder").GetInt32();
            object body = optionCollision
                ? new { questions = new[] { new { id = a.GetProperty("id").GetGuid(), options = new object[] { new { id = touchedId, label = "Unrelated change" }, new { id = changedId, sortOrder } } } } }
                : new { questions = new object[] { new { id = touchedId, title = "Unrelated change" }, new { id = changedId, sortOrder } } };
            var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(body));
            await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            using var error = await ReadJsonAsync(patch);
            var detail = error.RootElement.GetProperty("error").GetProperty("details").EnumerateArray().Single(item => item.GetProperty("code").GetString() == "DUPLICATE_SORT_ORDER");
            Assert.AreEqual(optionCollision ? "questions[0].options[1].sortOrder" : "questions[1].sortOrder", detail.GetProperty("path").GetString());
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId));
        }
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private async Task<JsonDocument> GetFormJsonAsync(Guid formId, string cookie)
    {
        var response = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", cookie);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private async Task<Guid> CreateConfiguredQuestionAsync(string cookie, string type, JsonElement config)
    {
        var response = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", cookie,
            JsonContent.Create(new { title = "Configured draft", questions = new[] { new { type, title = "Q", sortOrder = 0, config } } }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("id").GetGuid();
    }
}
