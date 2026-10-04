using System.Data.Common;
using System.Diagnostics;
using System.Net;
using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow("single", false, 409)]
    [DataRow("single", true, 201)]
    [DataRow("multiple", false, 201)]
    public async Task R8_concurrent_submissions_observe_waiter_and_enforce_mode(string mode, bool differentUser, int secondStatus)
    {
        var gate = new R8SaveGate("submit");
        await UseR8FactoryAsync(interceptors: [gate]);
        var owner = await CreateMemberAsync("R8 Single Concurrency");
        var respondent = differentUser ? await CreateMemberAsync("R8 Second Respondent") : owner;
        var form = await CreateR8FormAsync(owner.Cookie, mode: mode);
        gate.FormId = form.Id;
        var results = await RunR8OrderedAsync(gate,
            token => R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form), token),
            token => R8SubmitAsync(form, respondent.Cookie, "{\"answers\":[]}", token));
        Assert.AreEqual(HttpStatusCode.Created, results.First.StatusCode, await results.First.Content.ReadAsStringAsync());
        Assert.AreEqual((HttpStatusCode)secondStatus, results.Second.StatusCode, await results.Second.Content.ReadAsStringAsync());
        if (secondStatus == 409) await AssertErrorAsync(results.Second, HttpStatusCode.Conflict, "RESPONSE_ALREADY_SUBMITTED");
        Assert.AreEqual(secondStatus == 409 ? 1L : 2L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(5L, await R8AnswerCountAsync(form.Id));
    }

    [TestMethod]
    [DataRow("submit", "close", 200, "")]
    [DataRow("close", "submit", 409, "FORM_NOT_SUBMITTABLE")]
    [DataRow("submit", "delete", 204, "")]
    [DataRow("delete", "submit", 404, "FORM_NOT_FOUND")]
    public async Task R8_submit_close_delete_share_lock_and_read_state_after_wait(string firstAction, string secondAction, int secondStatus, string errorCode)
    {
        var gate = new R8SaveGate(firstAction);
        await UseR8FactoryAsync(interceptors: [gate]);
        var owner = await CreateMemberAsync("R8 Lifecycle Concurrency");
        var form = await CreateR8FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(form.Id);
        gate.FormId = form.Id;
        var results = await RunR8OrderedAsync(gate,
            token => R8ActionAsync(firstAction, form, owner.Cookie, token),
            token => R8ActionAsync(secondAction, form, owner.Cookie, token));
        Assert.AreEqual(firstAction == "submit" ? HttpStatusCode.Created : firstAction == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.OK,
            results.First.StatusCode, await results.First.Content.ReadAsStringAsync());
        Assert.AreEqual((HttpStatusCode)secondStatus, results.Second.StatusCode, await results.Second.Content.ReadAsStringAsync());
        if (errorCode.Length > 0) await AssertErrorAsync(results.Second, (HttpStatusCode)secondStatus, errorCode);
        var after = await ReadDefinitionRowsAsync(form.Id);
        AssertOnlyFormColumnsChanged(before, after, firstAction == "delete" || secondAction == "delete"
            ? ["deleted_at", "updated_at"] : ["status", "updated_at"]);
        Assert.AreEqual(firstAction == "submit" ? 1L : 0L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(firstAction == "submit" ? 5L : 0L, await R8AnswerCountAsync(form.Id));
        if (firstAction == "submit")
        {
            using var receipt = await ReadJsonAsync(results.First);
            await AssertR8PersistedReceiptAsync(receipt.RootElement);
        }
    }

    [TestMethod]
    public async Task R8_clock_samples_after_observed_lock_wait_crossing_deadline()
    {
        var clock = new R8Clock();
        await UseR8FactoryAsync(clock);
        var owner = await CreateMemberAsync("R8 Wait Clock");
        var deadline = clock.Now.AddSeconds(5);
        var form = await CreateR8FormAsync(owner.Cookie, deadline: deadline);
        await using var blocker = new NpgsqlConnection(BuildConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT id FROM forms WHERE id=@id FOR UPDATE", blocker, transaction))
        {
            command.Parameters.AddWithValue("id", form.Id);
            await command.ExecuteScalarAsync();
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        clock.Reset();
        var submit = R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}", cancellation.Token);
        try
        {
            await WaitForR8BlockedAsync(blocker.ProcessID);
            Assert.AreEqual(0, clock.Calls, "Submit cannot sample the business clock while waiting for its Form lock.");
            clock.Now = deadline;
            await transaction.CommitAsync();
            await AssertErrorAsync(await submit.WaitAsync(TimeSpan.FromSeconds(15)), HttpStatusCode.Conflict, "SUBMISSION_DEADLINE_EXCEEDED");
            Assert.AreEqual(1, clock.Calls);
            Assert.AreEqual(0L, await R8ResponseCountAsync(form.Id));
        }
        finally
        {
            await transaction.DisposeAsync();
            cancellation.Cancel();
            await DrainR8RequestsAsync(submit);
        }
    }

    [TestMethod]
    public async Task R8_clock_does_not_resample_when_save_and_commit_cross_deadline()
    {
        var clock = new R8Clock();
        var gate = new R8SaveGate("submit");
        await UseR8FactoryAsync(clock, [gate]);
        var owner = await CreateMemberAsync("R8 Delayed Commit");
        var deadline = clock.Now.AddSeconds(5);
        var form = await CreateR8FormAsync(owner.Cookie, deadline: deadline);
        var sampled = clock.Now;
        clock.Reset();
        gate.FormId = form.Id;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var submit = R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}", cancellation.Token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual(1, clock.Calls);
            clock.Now = deadline.AddSeconds(1);
            gate.Release.TrySetResult();
            var result = await submit.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual(HttpStatusCode.Created, result.StatusCode, await result.Content.ReadAsStringAsync());
            using var receipt = await ReadJsonAsync(result);
            Assert.AreEqual(sampled, receipt.RootElement.GetProperty("submittedAt").GetDateTimeOffset());
            Assert.AreEqual(1, clock.Calls);
            await AssertR8PersistedReceiptAsync(receipt.RootElement);
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
            cancellation.Cancel();
            await DrainR8RequestsAsync(submit);
        }
    }

    [TestMethod]
    [DataRow("draft")]
    [DataRow("published")]
    [DataRow("closed")]
    public async Task R8_delete_keeps_owner_rules_all_states_children_and_retry_behavior(string status)
    {
        var owner = await CreateMemberAsync("R8 Delete Owner");
        var other = await CreateMemberAsync("R8 Delete Other");
        var form = await CreateR8FormAsync(owner.Cookie);
        await ExecuteAsync("UPDATE forms SET status=CAST(@status AS form_status) WHERE id=@id",
            new NpgsqlParameter("status", status), new NpgsqlParameter("id", form.Id));
        var before = await ReadDefinitionRowsAsync(form.Id);
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{form.Id}", other.Cookie),
            HttpStatusCode.Forbidden, "FORM_ACCESS_DENIED");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(form.Id));
        Assert.AreEqual(HttpStatusCode.NoContent, (await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{form.Id}", owner.Cookie)).StatusCode);
        AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(form.Id), "deleted_at", "updated_at");
        await AssertErrorAsync(await SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{form.Id}", owner.Cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
    }

    private Task<HttpResponseMessage> R8ActionAsync(string action, R8Form form, string cookie, CancellationToken cancellationToken) =>
        action == "submit" ? R8SubmitAsync(form, cookie, R8AllAnswers(form), cancellationToken)
        : R8RequestAsync(action == "delete" ? HttpMethod.Delete : HttpMethod.Post,
            $"/api/v1/forms/{form.Id}" + (action == "delete" ? "" : "/close"), cookie, null, cancellationToken);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task R8_request_cleanup_observes_remaining_requests_and_reports_all_failures(bool firstTimesOut)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFailure = new InvalidOperationException("First request failed.");
        var laterFailure = new InvalidOperationException("Later request failed.");
        var later = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = firstTimesOut ? pending.Task : Task.FromException<HttpResponseMessage>(firstFailure);
        var drain = DrainR8RequestsAsync(first, later.Task);
        try
        {
            Assert.IsFalse(drain.IsCompleted, "Cleanup must continue to the remaining request after an earlier failure.");
            later.SetException(laterFailure);
            var aggregate = await Assert.ThrowsAsync<AggregateException>(async () =>
                await drain.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.HasCount(2, aggregate.InnerExceptions);
            Assert.AreSame(laterFailure, aggregate.InnerExceptions[1]);
            if (firstTimesOut) Assert.IsInstanceOfType<TimeoutException>(aggregate.InnerExceptions[0]);
            else Assert.AreSame(firstFailure, aggregate.InnerExceptions[0]);
            Console.WriteLine($"R8 cleanup evidence: first={(firstTimesOut ? "timeout" : "fault")}, later=fault; observed both requests; aggregate failures=2.");
        }
        finally
        {
            pending.TrySetCanceled();
            later.TrySetException(laterFailure);
            // Observe intentionally faulted test tasks even if the assertion detects a regression.
            foreach (var task in new[] { first, later.Task, drain })
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception) { }
            }
        }
    }

    [TestMethod]
    public async Task R8_request_cleanup_only_ignores_actual_task_cancellation()
    {
        var failure = new OperationCanceledException("Faulted request, not a canceled task.");
        var canceled = Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        var faulted = Task.FromException<HttpResponseMessage>(failure);
        var aggregate = await Assert.ThrowsAsync<AggregateException>(async () =>
            await DrainR8RequestsAsync(canceled, faulted));
        Assert.HasCount(1, aggregate.InnerExceptions);
        Assert.AreSame(failure, aggregate.InnerExceptions[0]);
        Console.WriteLine("R8 cleanup evidence: canceled task accepted; faulted OperationCanceledException reported, aggregate failures=1.");
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RunR8OrderedAsync(
        R8SaveGate gate, Func<CancellationToken, Task<HttpResponseMessage>> firstSend,
        Func<CancellationToken, Task<HttpResponseMessage>> secondSend)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var first = firstSend(cancellation.Token);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var blocker = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = secondSend(cancellation.Token);
            await WaitForR8BlockedAsync(blocker);
            Assert.IsFalse(second.IsCompleted);
            gate.Release.TrySetResult();
            return (await first.WaitAsync(TimeSpan.FromSeconds(15)), await second.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
            cancellation.Cancel();
            await DrainR8RequestsAsync(second is null ? [first] : [first, second]);
        }
    }

    private static async Task DrainR8RequestsAsync(params Task<HttpResponseMessage>[] requests)
    {
        // Bound cleanup and observe every request; a timeout is a failure, not concurrency evidence.
        var failures = new List<Exception>();
        foreach (var request in requests)
        {
            try { await request.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) when (request.IsCanceled) { }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
            throw new AggregateException("R8 request cleanup failed after observing all remaining requests.", failures);
    }

    private async Task WaitForR8BlockedAsync(int blockerPid)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            var waiter = await ScalarAsync<int>("""
                SELECT COALESCE((SELECT pid FROM pg_stat_activity
                  WHERE @blocker = ANY(pg_blocking_pids(pid))
                    AND wait_event_type='Lock' AND query LIKE '%FOR UPDATE%' LIMIT 1), 0)
                """, new NpgsqlParameter("blocker", blockerPid));
            if (waiter != 0)
            {
                Console.WriteLine($"R8 PostgreSQL waiter observed: blocker={blockerPid}, waiter={waiter}, wait_event_type=Lock, query=Form FOR UPDATE");
                return;
            }
            await Task.Delay(25);
        }
        Assert.Fail("No actual PostgreSQL Form lock waiter was observed within 10 seconds.");
    }

    private sealed class R8SaveGate(string action) : SaveChangesInterceptor
    {
        public Guid? FormId { get; set; }
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var database = eventData.Context!;
            if (FormId is { } id && (action == "submit"
                ? database.ChangeTracker.Entries<Response>().Any(entry => entry.Entity.FormId == id)
                : database.ChangeTracker.Entries<Form>().Any(entry => entry.Entity.Id == id))
                && Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Reached.TrySetResult(((NpgsqlConnection)database.Database.GetDbConnection()).ProcessID);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(35), cancellationToken);
            }
            return result;
        }
    }
}
