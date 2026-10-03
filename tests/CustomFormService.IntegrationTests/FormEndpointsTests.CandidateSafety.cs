using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    public async Task Invalid_candidate_never_mutates_tracked_definition_or_adds_entities()
    {
        var owner = await CreateMemberAsync("R6 Candidate Isolation Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(formId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var form = await database.Forms.Include(form => form.Questions).ThenInclude(question => question.Options)
            .AsSplitQuery().SingleAsync(form => form.Id == formId);
        var question = form.Questions.OrderBy(question => question.SortOrder).First();
        var option = question.Options.OrderBy(option => option.SortOrder).First();
        var originalValue = option.Value;
        var originalTitle = question.Title;
        var entryCount = database.ChangeTracker.Entries().Count();
        var candidate = FormDefinitionCandidate.From(form);
        var delta = JsonSerializer.SerializeToElement(new object[]
        {
            new { type = "single_choice", title = "New", sortOrder = 3, options = new[] { new { label = "New", value = " new ", sortOrder = 0 } } },
            new { id = question.Id, title = " ", options = new[] { new { id = option.Id, value = " replacement " } } }
        });
        Assert.IsTrue(DraftDefinitionDelta.TryApplyQuestions(candidate, delta, out _));
        Assert.IsFalse(FormDefinitionValidator.TryNormalizeQuestions(candidate.Questions, out var errors));
        Assert.IsTrue(errors.Any(error => error.Path == "questions[1].title" && error.Code == "REQUIRED_FIELD"));
        Assert.AreEqual(originalTitle, question.Title);
        Assert.AreEqual(originalValue, option.Value);
        Assert.HasCount(3, form.Questions);
        Assert.AreEqual(entryCount, database.ChangeTracker.Entries().Count());
        Assert.IsFalse(database.ChangeTracker.HasChanges());
        Assert.IsTrue(database.ChangeTracker.Entries().All(entry => entry.State == EntityState.Unchanged));
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(formId));
    }

    [TestMethod]
    [DataRow("question")]
    [DataRow("sortOrder")]
    [DataRow("value")]
    public async Task Candidate_collision_paths_prefer_causal_fields_over_explicit_unchanged_fields(string mode)
    {
        var owner = await CreateMemberAsync("R6 Causal Source Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var questions = before.RootElement.GetProperty("questions");
        var a = questions[0];
        var rows = await ReadDefinitionRowsAsync(formId);
        for (var direction = 0; direction < 2; direction++)
        {
            var changed = mode == "question" ? questions[direction] : a.GetProperty("options")[direction];
            var unchanged = mode == "question" ? questions[1 - direction] : a.GetProperty("options")[1 - direction];
            var changedId = changed.GetProperty("id").GetGuid();
            var unchangedId = unchanged.GetProperty("id").GetGuid();
            var sortOrder = unchanged.GetProperty("sortOrder").GetInt32();
            var value = unchanged.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
            object body = mode == "question"
                ? new { questions = new object[] { new { id = changedId, sortOrder }, new { id = unchangedId, sortOrder, title = "Unrelated" } } }
                : new { questions = new[] { new { id = a.GetProperty("id").GetGuid(), options = mode == "value"
                    ? new object[] { new { id = changedId, value }, new { id = unchangedId, value, label = "Unrelated" } }
                    : new object[] { new { id = changedId, sortOrder }, new { id = unchangedId, sortOrder, label = "Unrelated" } } } } };
            var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(body));
            await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "FORM_DEFINITION_INVALID");
            using var error = await ReadJsonAsync(patch);
            var detail = error.RootElement.GetProperty("error").GetProperty("details").EnumerateArray()
                .Single(item => item.GetProperty("code").GetString() == (mode == "value" ? "INVALID_OPTION" : "DUPLICATE_SORT_ORDER"));
            Assert.AreEqual(mode == "question" ? "questions[0].sortOrder" : $"questions[0].options[0].{mode}", detail.GetProperty("path").GetString());
            Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId));
        }
    }

    [TestMethod]
    public async Task Patch_tracks_incoming_options_after_first_save_and_returns_actual_database_values()
    {
        var probe = new SaveBoundaryProbe();
        await UseCandidateInterceptorsAsync(probe);
        var owner = await CreateMemberAsync("R6 Save Boundary Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        using var beforeRows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        var a = before.RootElement.GetProperty("questions")[0];
        var optionId = a.GetProperty("options")[0].GetProperty("id").GetGuid();
        probe.Armed = true;
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie, JsonContent.Create(new
        {
            submissionDeadlineAt = "2026-12-31T00:00:00.1234567Z",
            questions = new object[]
            {
                new { id = a.GetProperty("id").GetGuid(), options = new[] { new { id = optionId, value = " updated " } } },
                new { type = "single_choice", title = "New", sortOrder = 3, options = new[] { new { label = "New", value = " new ", sortOrder = 0 } } }
            }
        }));
        probe.Armed = false;
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
        Assert.AreEqual(2, probe.CompletedSaves);
        Assert.IsTrue(probe.FirstAutoDetectEnabled);
        Assert.AreEqual(0, probe.FirstAddedOptions);
        Assert.IsFalse(probe.FirstGraphValues.Contains("updated"));
        Assert.IsFalse(probe.FirstGraphValues.Contains("new"));
        Assert.AreEqual(2, probe.SecondAddedOptions);
        using var response = await ReadJsonAsync(patch);
        using var actual = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.AreEqual(actual.RootElement.GetRawText(), response.RootElement.GetRawText(), "PATCH must return DB values, including microsecond precision and JSONB representation.");
        using var rows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        AssertApiDefinitionMatchesRows(response.RootElement, rows.RootElement);
        Assert.AreEqual(FindRow(beforeRows.RootElement, "options", optionId).GetProperty("created_at").GetDateTimeOffset(),
            FindRow(rows.RootElement, "options", optionId).GetProperty("created_at").GetDateTimeOffset());
        Assert.AreEqual("updated", FindRow(rows.RootElement, "options", optionId).GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task Patch_does_not_return_success_and_rolls_back_when_commit_fails()
    {
        var probe = new SaveBoundaryProbe();
        var failure = new CommitFailureInterceptor();
        await UseCandidateInterceptorsAsync(probe, failure);
        var owner = await CreateMemberAsync("R6 Commit Failure Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var before = await GetFormJsonAsync(formId, owner.Cookie);
        var rows = await ReadDefinitionRowsAsync(formId);
        var a = before.RootElement.GetProperty("questions")[0];
        probe.Armed = failure.Armed = true;
        var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { title = "Must roll back at commit", questions = new[] { new
            {
                id = a.GetProperty("id").GetGuid(), options = new[] { new { id = a.GetProperty("options")[0].GetProperty("id").GetGuid(), value = " replacement " } }
            } } }));
        probe.Armed = failure.Armed = false;
        await AssertErrorAsync(patch, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        Assert.AreEqual(2, probe.CompletedSaves, "Both save stages must complete before the commit fault.");
        Assert.IsTrue(failure.Injected);
        using var after = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.AreEqual(before.RootElement.GetRawText(), after.RootElement.GetRawText());
        Assert.AreEqual(rows, await ReadDefinitionRowsAsync(formId), "Commit failure must restore all rows, IDs and timestamps.");
    }

    private async Task UseCandidateInterceptorsAsync(params IInterceptor[] interceptors)
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new CustomFormServiceApiFactory(interceptors: interceptors);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    }

    private sealed class SaveBoundaryProbe : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int CompletedSaves { get; private set; }
        public int FirstAddedOptions { get; private set; }
        public int SecondAddedOptions { get; private set; }
        public bool FirstAutoDetectEnabled { get; private set; }
        public string[] FirstGraphValues { get; private set; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                var context = eventData.Context!;
                var added = context.ChangeTracker.Entries<Option>().Count(entry => entry.State == EntityState.Added);
                if (CompletedSaves == 0)
                {
                    FirstAutoDetectEnabled = context.ChangeTracker.AutoDetectChangesEnabled;
                    FirstAddedOptions = added;
                    FirstGraphValues = context.ChangeTracker.Entries<Question>().SelectMany(entry => entry.Entity.Options).Select(option => option.Value).ToArray();
                }
                else SecondAddedOptions = added;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed) CompletedSaves++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                await using var command = transaction.Connection!.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT 1 / 0;";
                Injected = true;
                await command.ExecuteScalarAsync(cancellationToken);
            }
            return result;
        }
    }
}
