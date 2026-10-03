using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    public async Task Patch_normalizes_new_option_values_in_existing_question_before_tracking()
    {
        var owner = await CreateMemberAsync("R6 Normalize Option Owner");
        var formId = await CreateNestedFormAsync(owner.Cookie, "Normalize Option");
        var before = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        using var beforeBody = await ReadJsonAsync(before);
        var questionId = beforeBody.RootElement.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var originalOptionId = beforeBody.RootElement.GetProperty("questions")[0].GetProperty("options")[0].GetProperty("id").GetGuid();
        var originalRow = await ScalarAsync<string>("SELECT to_jsonb(o)::text FROM options o WHERE id = @id;", new NpgsqlParameter("id", originalOptionId));

        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new
            {
                questions = new[]
                {
                    new
                    {
                        id = questionId,
                        options = new[] { new { label = " Added ", value = " \u2003new value\u00a0 ", sortOrder = 1 } }
                    }
                }
            }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var patched = await ReadJsonAsync(patch);
        var added = patched.RootElement.GetProperty("questions")[0].GetProperty("options")[1];
        Assert.AreEqual("new value", added.GetProperty("value").GetString());
        Assert.AreEqual("Added", added.GetProperty("label").GetString());
        var addedId = added.GetProperty("id").GetGuid();
        Assert.AreNotEqual(originalOptionId, addedId);

        var detail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var persisted = await ReadJsonAsync(detail);
        Assert.AreEqual(added.GetRawText(), persisted.RootElement.GetProperty("questions")[0].GetProperty("options")[1].GetRawText());
        Assert.AreEqual("new value", await ScalarAsync<string>("SELECT value FROM options WHERE id = @id;", new NpgsqlParameter("id", addedId)));
        Assert.IsTrue(await ScalarAsync<bool>("SELECT created_at = updated_at FROM options WHERE id = @id;", new NpgsqlParameter("id", addedId)));
        Assert.AreEqual(originalRow, await ScalarAsync<string>("SELECT to_jsonb(o)::text FROM options o WHERE id = @id;", new NpgsqlParameter("id", originalOptionId)));

        // Normalized equality must be a no-op even though the raw request differs.
        var beforeNoop = await ReadDefinitionRowsAsync(formId);
        var noop = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id = questionId, options = new[] { new { id = addedId, value = " new value " } } } } }));
        Assert.AreEqual(HttpStatusCode.OK, noop.StatusCode, await noop.Content.ReadAsStringAsync());
        Assert.AreEqual(beforeNoop, await ReadDefinitionRowsAsync(formId));
    }

    [TestMethod]
    public async Task Patch_normalizes_option_values_in_new_question_and_rejects_normalized_duplicates()
    {
        var owner = await CreateMemberAsync("R6 Normalize Question Owner");
        var formId = await CreateSimpleFormAsync(owner.Cookie, "Normalize Question");
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new
            {
                questions = new[]
                {
                    new
                    {
                        type = "single_choice", title = " New choice ", sortOrder = 0,
                        options = new[]
                        {
                            new { label = " Alpha ", value = " \u2003Alpha\u00a0 ", sortOrder = 0 },
                            new { label = " Lowercase ", value = " alpha ", sortOrder = 1 }
                        }
                    }
                }
            }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var patched = await ReadJsonAsync(patch);
        var question = patched.RootElement.GetProperty("questions")[0];
        var questionId = question.GetProperty("id").GetGuid();
        Assert.AreEqual("New choice", question.GetProperty("title").GetString());
        CollectionAssert.AreEqual(new[] { "Alpha", "alpha" }, question.GetProperty("options").EnumerateArray().Select(option => option.GetProperty("value").GetString()).ToArray());
        var detail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var persisted = await ReadJsonAsync(detail);
        Assert.AreEqual(question.GetRawText(), persisted.RootElement.GetProperty("questions")[0].GetRawText());
        foreach (var option in question.GetProperty("options").EnumerateArray())
        {
            Assert.AreEqual(option.GetProperty("value").GetString(), await ScalarAsync<string>("SELECT value FROM options WHERE id = @id;", new NpgsqlParameter("id", option.GetProperty("id").GetGuid())));
        }
        Assert.IsTrue(await ScalarAsync<bool>(
            "SELECT q.created_at = q.updated_at AND bool_and(o.created_at = q.created_at AND o.updated_at = q.updated_at) FROM questions q JOIN options o ON o.question_id = q.id WHERE q.id = @id GROUP BY q.id;",
            new NpgsqlParameter("id", questionId)));

        var beforeInvalid = await ReadDefinitionRowsAsync(formId);
        var duplicate = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { questions = new[] { new { id = questionId, options = new[] { new { label = "Duplicate", value = " Alpha ", sortOrder = 2 } } } } }));
        await AssertErrorAsync(duplicate, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
        Assert.AreEqual(beforeInvalid, await ReadDefinitionRowsAsync(formId));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task Patch_persists_mixed_option_and_question_operations_atomically(int scenario)
    {
        var owner = await CreateMemberAsync($"R6 Mixed {scenario} Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        var beforeResponse = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        using var beforeBody = await ReadJsonAsync(beforeResponse);
        using var beforeRows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        var questions = beforeBody.RootElement.GetProperty("questions");
        var questionA = questions[0];
        var questionB = questions[1];
        var untouched = questions[2];
        var aId = questionA.GetProperty("id").GetGuid();
        var bId = questionB.GetProperty("id").GetGuid();
        var untouchedId = untouched.GetProperty("id").GetGuid();
        var changedOptionId = (scenario == 2 ? questionB : questionA).GetProperty("options")[0].GetProperty("id").GetGuid();
        var beforeChangedOption = FindRow(beforeRows.RootElement, "options", changedOptionId);

        object[] deltas = scenario switch
        {
            1 =>
            [
                new
                {
                    type = "single_choice", title = "New choice", sortOrder = 3,
                    options = new[] { new { label = " New ", value = " \u2003new\u00a0 ", sortOrder = 0 } }
                },
                new { id = aId, options = new[] { new { id = changedOptionId, value = " changed " } } }
            ],
            2 =>
            [
                new { id = aId },
                new { id = bId, options = new[] { new { id = changedOptionId, value = " changed " } } }
            ],
            3 =>
            [
                new
                {
                    id = aId,
                    options = new object[]
                    {
                        new { label = " New ", value = " new ", sortOrder = 3 },
                        new { id = changedOptionId, value = " changed " },
                        new { id = questionA.GetProperty("options")[2].GetProperty("id").GetGuid() }
                    }
                },
                new { id = bId, type = "text" }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { title = $"Mixed {scenario} saved", questions = deltas }));
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        using var patched = await ReadJsonAsync(patch);
        var detail = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var persisted = await ReadJsonAsync(detail);
        Assert.AreEqual(patched.RootElement.GetRawText(), persisted.RootElement.GetRawText(),
            "PATCH must return the same persisted fields and timestamp precision as GET.");
        using var afterRows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        AssertApiDefinitionMatchesRows(persisted.RootElement, afterRows.RootElement);
        var afterChangedOption = FindRow(afterRows.RootElement, "options", changedOptionId);
        Assert.AreEqual("changed", afterChangedOption.GetProperty("value").GetString());
        Assert.AreEqual(beforeChangedOption.GetProperty("created_at").GetString(), afterChangedOption.GetProperty("created_at").GetString());
        Assert.IsTrue(afterChangedOption.GetProperty("updated_at").GetDateTimeOffset() > beforeChangedOption.GetProperty("updated_at").GetDateTimeOffset());
        var persistedNow = afterRows.RootElement.GetProperty("form").GetProperty("updated_at").GetDateTimeOffset();
        Assert.AreEqual(persistedNow, afterChangedOption.GetProperty("updated_at").GetDateTimeOffset());
        foreach (var row in afterRows.RootElement.GetProperty("questions").EnumerateArray().Where(row => row.GetProperty("id").GetGuid() != untouchedId))
        {
            var id = row.GetProperty("id").GetGuid();
            if (id == aId || scenario != 1 && id == bId)
            {
                Assert.AreEqual(FindRow(beforeRows.RootElement, "questions", id).GetProperty("created_at").GetDateTimeOffset(), row.GetProperty("created_at").GetDateTimeOffset());
                Assert.AreEqual(persistedNow, row.GetProperty("updated_at").GetDateTimeOffset());
            }
            else if (id != bId)
            {
                Assert.AreEqual(persistedNow, row.GetProperty("created_at").GetDateTimeOffset());
                Assert.AreEqual(persistedNow, row.GetProperty("updated_at").GetDateTimeOffset());
            }
        }
        var beforeOptionIds = beforeRows.RootElement.GetProperty("options").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        foreach (var row in afterRows.RootElement.GetProperty("options").EnumerateArray())
        {
            var id = row.GetProperty("id").GetGuid();
            if (!beforeOptionIds.Contains(id))
            {
                Assert.AreEqual("New", row.GetProperty("label").GetString());
                Assert.AreEqual(persistedNow, row.GetProperty("created_at").GetDateTimeOffset());
                Assert.AreEqual(persistedNow, row.GetProperty("updated_at").GetDateTimeOffset());
            }
            else if (id != changedOptionId)
            {
                Assert.AreEqual(FindRow(beforeRows.RootElement, "options", id).GetRawText(), row.GetRawText(), "Unmodified Option fields and timestamps must be retained.");
            }
        }
        Assert.AreEqual(FindRow(beforeRows.RootElement, "questions", untouchedId).GetRawText(), FindRow(afterRows.RootElement, "questions", untouchedId).GetRawText());
        var untouchedOptionId = untouched.GetProperty("options")[0].GetProperty("id").GetGuid();
        Assert.AreEqual(FindRow(beforeRows.RootElement, "options", untouchedOptionId).GetRawText(), FindRow(afterRows.RootElement, "options", untouchedOptionId).GetRawText());
        Assert.AreEqual(beforeRows.RootElement.GetProperty("form").GetProperty("created_at").GetString(), afterRows.RootElement.GetProperty("form").GetProperty("created_at").GetString());
        Assert.IsTrue(afterRows.RootElement.GetProperty("form").GetProperty("updated_at").GetDateTimeOffset() > beforeRows.RootElement.GetProperty("form").GetProperty("updated_at").GetDateTimeOffset());

        if (scenario == 1)
        {
            var addedQuestion = afterRows.RootElement.GetProperty("questions").EnumerateArray().Single(row => row.GetProperty("sort_order").GetInt32() == 3);
            var addedOption = afterRows.RootElement.GetProperty("options").EnumerateArray().Single(row => row.GetProperty("question_id").GetGuid() == addedQuestion.GetProperty("id").GetGuid());
            Assert.AreEqual("new", addedOption.GetProperty("value").GetString());
            Assert.AreEqual(addedQuestion.GetProperty("created_at").GetString(), addedOption.GetProperty("created_at").GetString());
            Assert.AreEqual(addedOption.GetProperty("created_at").GetString(), addedOption.GetProperty("updated_at").GetString());
            Assert.AreEqual(FindRow(beforeRows.RootElement, "questions", bId).GetRawText(), FindRow(afterRows.RootElement, "questions", bId).GetRawText());
        }
        else if (scenario == 2)
        {
            Assert.IsFalse(afterRows.RootElement.GetProperty("questions").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == aId));
            Assert.IsFalse(afterRows.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("question_id").GetGuid() == aId));
            Assert.AreEqual(0L, await ScalarAsync<long>("SELECT count(*) FROM options WHERE question_id = @id;", new NpgsqlParameter("id", aId)));
        }
        else
        {
            var changedB = FindRow(afterRows.RootElement, "questions", bId);
            Assert.AreEqual("text", changedB.GetProperty("type").GetString());
            Assert.AreEqual(0, changedB.GetProperty("config").EnumerateObject().Count());
            Assert.IsFalse(afterRows.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("question_id").GetGuid() == bId));
            Assert.IsFalse(afterRows.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == questionA.GetProperty("options")[2].GetProperty("id").GetGuid()));
            Assert.AreEqual("new", afterRows.RootElement.GetProperty("options").EnumerateArray().Single(row => row.GetProperty("question_id").GetGuid() == aId && row.GetProperty("sort_order").GetInt32() == 3).GetProperty("value").GetString());
        }
    }

    [TestMethod]
    public async Task Patch_rolls_back_first_save_when_second_save_has_database_failure()
    {
        var failure = new SecondSaveFailureInterceptor();
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new CustomFormServiceApiFactory(interceptors: [failure]);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var owner = await CreateMemberAsync("R6 Rollback Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        var beforeResponse = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, beforeResponse.StatusCode);
        var beforeApi = await beforeResponse.Content.ReadAsStringAsync();
        using var beforeBody = JsonDocument.Parse(beforeApi);
        var beforeDatabase = await ReadDefinitionRowsAsync(formId);
        using var beforeRows = JsonDocument.Parse(beforeDatabase);
        var questionA = beforeBody.RootElement.GetProperty("questions")[0];
        var questionB = beforeBody.RootElement.GetProperty("questions")[1];
        var aId = questionA.GetProperty("id").GetGuid();
        var bId = questionB.GetProperty("id").GetGuid();
        var changedOptionId = questionA.GetProperty("options")[0].GetProperty("id").GetGuid();

        failure.Arm(formId);
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new
            {
                title = "Must roll back",
                questions = new object[]
                {
                    new
                    {
                        id = aId, title = "A changed",
                        options = new object[]
                        {
                            new { id = changedOptionId, value = " replacement " },
                            new { id = questionA.GetProperty("options")[1].GetProperty("id").GetGuid(), label = "Label changed" },
                            new { id = questionA.GetProperty("options")[2].GetProperty("id").GetGuid() },
                            new { label = "New option", value = " new ", sortOrder = 3 }
                        }
                    },
                    new { id = bId, type = "text" },
                    new
                    {
                        type = "single_choice", title = "New question", sortOrder = 3,
                        options = new[] { new { label = "Nested", value = " nested ", sortOrder = 0 } }
                    }
                }
            }));
        await AssertErrorAsync(patch, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        using (var error = await ReadJsonAsync(patch))
        {
            Assert.AreEqual(2, error.RootElement.GetProperty("error").EnumerateObject().Count(), "No SQL, exception, or test fault details may leak.");
        }
        Assert.AreEqual(2, failure.Attempts);
        Assert.AreEqual(1, failure.CompletedSaves, "The first SaveChanges must actually complete before the injected failure.");
        Assert.IsTrue(failure.DatabaseFailureInjected);
        Assert.IsNotNull(failure.AfterFirstSave);
        using var intermediate = JsonDocument.Parse(failure.AfterFirstSave);
        Assert.AreEqual("Must roll back", intermediate.RootElement.GetProperty("form").GetProperty("title").GetString());
        Assert.IsFalse(intermediate.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == changedOptionId));
        Assert.AreEqual("text", FindRow(intermediate.RootElement, "questions", bId).GetProperty("type").GetString());
        Assert.AreEqual(0, FindRow(intermediate.RootElement, "questions", bId).GetProperty("config").EnumerateObject().Count());
        Assert.IsFalse(intermediate.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("question_id").GetGuid() == bId));
        var labelChangedId = questionA.GetProperty("options")[1].GetProperty("id").GetGuid();
        Assert.AreEqual("Label changed", FindRow(intermediate.RootElement, "options", labelChangedId).GetProperty("label").GetString());
        var deletedId = questionA.GetProperty("options")[2].GetProperty("id").GetGuid();
        Assert.IsFalse(intermediate.RootElement.GetProperty("options").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == deletedId));
        Assert.AreNotEqual(beforeRows.RootElement.GetProperty("form").GetProperty("updated_at").GetString(), intermediate.RootElement.GetProperty("form").GetProperty("updated_at").GetString());

        // New Question rows were inserted by save #1; new/replacement Options were
        // staged for save #2. Neither may leave residues after the transaction aborts.
        var oldQuestionIds = beforeRows.RootElement.GetProperty("questions").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        var newQuestionIds = intermediate.RootElement.GetProperty("questions").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Where(id => !oldQuestionIds.Contains(id)).ToArray();
        Assert.HasCount(1, newQuestionIds);
        var oldOptionIds = beforeRows.RootElement.GetProperty("options").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        var newOptionIds = failure.SecondSaveOptionIds.Where(id => !oldOptionIds.Contains(id)).ToArray();
        Assert.HasCount(2, newOptionIds);
        failure.Disarm();
        var after = await SendWithCookieAsync(HttpMethod.Get, $"/api/v1/forms/{formId}", owner.Cookie);
        Assert.AreEqual(HttpStatusCode.OK, after.StatusCode);
        Assert.AreEqual(beforeApi, await after.Content.ReadAsStringAsync(), "Definition IDs and Form createdAt/updatedAt must be fully restored.");
        Assert.AreEqual(beforeDatabase, await ReadDefinitionRowsAsync(formId), "Compare every persisted field, including all Question/Option IDs and timestamps.");
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT count(*) FROM questions WHERE id = ANY(@ids);", new NpgsqlParameter("ids", newQuestionIds)));
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT count(*) FROM options WHERE id = ANY(@ids);", new NpgsqlParameter("ids", newOptionIds)));
    }

    private async Task<Guid> CreateMixedOperationFormAsync(string cookie)
    {
        var create = await SendWithCookieAsync(HttpMethod.Post, "/api/v1/forms", cookie,
            JsonContent.Create(new
            {
                title = "Mixed baseline",
                questions = new[]
                {
                    new
                    {
                        type = "single_choice", title = "A", sortOrder = 0, config = new { variant = "select" },
                        options = new[]
                        {
                            new { label = "A0", value = "a0", sortOrder = 0 },
                            new { label = "A1", value = "a1", sortOrder = 1 },
                            new { label = "A2", value = "a2", sortOrder = 2 }
                        }
                    },
                    new
                    {
                        type = "single_choice", title = "B", sortOrder = 1, config = new { variant = "select" },
                        options = new[] { new { label = "B0", value = "b0", sortOrder = 0 } }
                    },
                    new
                    {
                        type = "single_choice", title = "Untouched", sortOrder = 2, config = new { variant = "radio" },
                        options = new[] { new { label = "Untouched", value = "untouched", sortOrder = 0 } }
                    }
                }
            }));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        using var created = await ReadJsonAsync(create);
        var formId = created.RootElement.GetProperty("id").GetGuid();
        // Fixed baseline makes retained creation times and changed update times observable.
        await ExecuteAsync("""
            UPDATE forms SET created_at = @baseline, updated_at = @baseline WHERE id = @formId;
            UPDATE questions SET created_at = @baseline, updated_at = @baseline WHERE form_id = @formId;
            UPDATE options SET created_at = @baseline, updated_at = @baseline WHERE question_id IN (SELECT id FROM questions WHERE form_id = @formId);
            """, new NpgsqlParameter("formId", formId), new NpgsqlParameter("baseline", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        return formId;
    }

    private static JsonElement FindRow(JsonElement rows, string table, Guid id) =>
        rows.GetProperty(table).EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == id);

    private static void AssertApiDefinitionMatchesRows(JsonElement detail, JsonElement rows)
    {
        var form = rows.GetProperty("form");
        Assert.AreEqual(detail.GetProperty("id").GetGuid(), form.GetProperty("id").GetGuid());
        Assert.AreEqual(detail.GetProperty("title").GetString(), form.GetProperty("title").GetString());
        foreach (var (apiName, column) in new[]
        {
            ("userId", "user_id"), ("description", "description"), ("status", "status"),
            ("responseMode", "response_mode"), ("responseIdentity", "response_identity"),
            ("resultVisibility", "result_visibility"), ("submissionDeadlineAt", "submission_deadline_at")
        })
        {
            Assert.IsTrue(JsonElement.DeepEquals(detail.GetProperty(apiName), form.GetProperty(column)), $"Form field {apiName} differs from DB.");
        }
        Assert.AreEqual(detail.GetProperty("createdAt").GetDateTimeOffset(), form.GetProperty("created_at").GetDateTimeOffset());
        Assert.AreEqual(detail.GetProperty("updatedAt").GetDateTimeOffset(), form.GetProperty("updated_at").GetDateTimeOffset());
        Assert.AreEqual(detail.GetProperty("questions").GetArrayLength(), rows.GetProperty("questions").GetArrayLength());
        foreach (var question in detail.GetProperty("questions").EnumerateArray())
        {
            var id = question.GetProperty("id").GetGuid();
            var row = FindRow(rows, "questions", id);
            Assert.AreEqual(question.GetProperty("title").GetString(), row.GetProperty("title").GetString());
            Assert.AreEqual(question.GetProperty("type").GetString(), row.GetProperty("type").GetString());
            Assert.AreEqual(detail.GetProperty("id").GetGuid(), row.GetProperty("form_id").GetGuid());
            Assert.AreEqual(question.GetProperty("description").GetString(), row.GetProperty("description").GetString());
            Assert.AreEqual(question.GetProperty("required").GetBoolean(), row.GetProperty("required").GetBoolean());
            Assert.AreEqual(question.GetProperty("sortOrder").GetInt32(), row.GetProperty("sort_order").GetInt32());
            Assert.IsTrue(JsonElement.DeepEquals(question.GetProperty("config"), row.GetProperty("config")), "Question Config differs from DB.");
            Assert.AreEqual(question.GetProperty("options").GetArrayLength(), rows.GetProperty("options").EnumerateArray().Count(option => option.GetProperty("question_id").GetGuid() == id));
            foreach (var option in question.GetProperty("options").EnumerateArray())
            {
                var optionRow = FindRow(rows, "options", option.GetProperty("id").GetGuid());
                Assert.AreEqual(id, optionRow.GetProperty("question_id").GetGuid());
                Assert.AreEqual(option.GetProperty("label").GetString(), optionRow.GetProperty("label").GetString());
                Assert.AreEqual(option.GetProperty("value").GetString(), optionRow.GetProperty("value").GetString());
                Assert.AreEqual(option.GetProperty("sortOrder").GetInt32(), optionRow.GetProperty("sort_order").GetInt32());
            }
        }
    }

    private const string DefinitionRowsSql = """
        SELECT jsonb_build_object(
            'form', (SELECT to_jsonb(f) FROM forms f WHERE f.id = @formId),
            'questions', COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY q.id) FROM questions q WHERE q.form_id = @formId), '[]'::jsonb),
            'options', COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.id) FROM options o JOIN questions q ON q.id = o.question_id WHERE q.form_id = @formId), '[]'::jsonb)
        )::text;
        """;

    private Task<string> ReadDefinitionRowsAsync(Guid formId) =>
        ScalarAsync<string>(DefinitionRowsSql, new NpgsqlParameter("formId", formId));

    private sealed class SecondSaveFailureInterceptor : SaveChangesInterceptor
    {
        private Guid? _formId;
        public int Attempts { get; private set; }
        public int CompletedSaves { get; private set; }
        public bool DatabaseFailureInjected { get; private set; }
        public string? AfterFirstSave { get; private set; }
        public Guid[] SecondSaveOptionIds { get; private set; } = [];

        public void Arm(Guid formId) => _formId = formId;
        public void Disarm() => _formId = null;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_formId is null)
            {
                return result;
            }

            Attempts++;
            if (Attempts == 2)
            {
                var context = eventData.Context!;
                SecondSaveOptionIds = context.ChangeTracker.Entries<Option>()
                    .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id).ToArray();
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "SELECT 1 / 0;";
                DatabaseFailureInjected = true;
                // A real PostgreSQL error in the outer transaction, without schema changes.
                await command.ExecuteScalarAsync(cancellationToken);
            }

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_formId is null)
            {
                return result;
            }

            CompletedSaves++;
            if (Attempts == 1)
            {
                var context = eventData.Context!;
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = DefinitionRowsSql;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "formId";
                parameter.Value = _formId.Value;
                command.Parameters.Add(parameter);
                AfterFirstSave = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
            }

            return result;
        }
    }
}
