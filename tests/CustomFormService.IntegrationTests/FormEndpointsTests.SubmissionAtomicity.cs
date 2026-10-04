using System.Data.Common;
using System.Net;
using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow("partial-answer")]
    [DataRow("pre-commit")]
    public async Task R8_submit_precommit_fault_rolls_back_actual_writes_and_single_waiter_can_succeed(string injection)
    {
        var gate = new R8SaveGate("submit");
        var evidence = new R8FaultEvidence();
        IInterceptor fault = injection == "partial-answer"
            ? new R8AfterWriteFault("submit", evidence) : new R8BeforeCommitFault("submit", evidence);
        await UseR8FactoryAsync(interceptors: [gate, fault], maxBatchSize: injection == "partial-answer" ? 1 : null);
        var owner = await CreateMemberAsync("R8 Fault Owner");
        var form = await CreateR8FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(form.Id);
        evidence.FormId = form.Id;
        gate.FormId = form.Id;
        var results = await RunR8OrderedAsync(gate,
            token => R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form), token),
            token => R8SubmitAsync(form, owner.Cookie, "{\"answers\":[]}", token));
        await AssertErrorAsync(results.First, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        Assert.AreEqual(HttpStatusCode.Created, results.Second.StatusCode, await results.Second.Content.ReadAsStringAsync());
        Assert.IsTrue(evidence.Injected);
        Assert.AreEqual(1L, evidence.ResponsesBeforeFailure);
        Assert.AreEqual(injection == "partial-answer" ? 1L : 5L, evidence.AnswersBeforeFailure);
        Assert.AreNotEqual(Guid.Empty, evidence.ResponseId);
        // Fresh SQL connection sees only the successful waiter, with no rows from the failed transaction.
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM responses WHERE id=@id", new NpgsqlParameter("id", evidence.ResponseId)));
        Assert.AreEqual(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM answers WHERE response_id=@id", new NpgsqlParameter("id", evidence.ResponseId)));
        Assert.AreEqual(1L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(0L, await R8AnswerCountAsync(form.Id));
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(form.Id));
        using var receipt = await ReadJsonAsync(results.Second);
        await AssertR8PersistedReceiptAsync(receipt.RootElement);
        Console.WriteLine($"R8 fault evidence: {injection}, actual EF inserts before failure responses={evidence.ResponsesBeforeFailure}, answers={evidence.AnswersBeforeFailure}; failed response residual=0, answer residual=0; observed single waiter=201.");
    }

    [TestMethod]
    [DataRow("after-update")]
    [DataRow("pre-commit")]
    public async Task R8_delete_precommit_fault_rolls_back_update_and_waiting_submit_can_succeed(string injection)
    {
        var gate = new R8SaveGate("delete");
        var evidence = new R8FaultEvidence();
        IInterceptor fault = injection == "after-update"
            ? new R8AfterWriteFault("delete", evidence) : new R8BeforeCommitFault("delete", evidence);
        await UseR8FactoryAsync(interceptors: [gate, fault]);
        var owner = await CreateMemberAsync("R8 Delete Rollback");
        var form = await CreateR8FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(form.Id);
        evidence.FormId = form.Id;
        gate.FormId = form.Id;
        var results = await RunR8OrderedAsync(gate,
            token => R8ActionAsync("delete", form, owner.Cookie, token),
            token => R8SubmitAsync(form, owner.Cookie, R8AllAnswers(form), token));
        await AssertErrorAsync(results.First, HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
        Assert.AreEqual(HttpStatusCode.Created, results.Second.StatusCode, await results.Second.Content.ReadAsStringAsync());
        Assert.IsTrue(evidence.Injected);
        Assert.IsTrue(evidence.DeletedInsideTransaction, "The UPDATE must have executed before injection.");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(form.Id));
        Assert.AreEqual(1L, await R8ResponseCountAsync(form.Id));
        Assert.AreEqual(5L, await R8AnswerCountAsync(form.Id));
        Console.WriteLine($"R8 fault evidence: Delete {injection}, actual deleted_at non-null inside transaction; fresh connection definition unchanged; observed Submit waiter=201.");
    }

    private sealed class R8FaultEvidence
    {
        public Guid? FormId { get; set; }
        public Guid ResponseId { get; private set; }
        public bool Injected { get; private set; }
        public long ResponsesBeforeFailure { get; private set; }
        public long AnswersBeforeFailure { get; private set; }
        public bool DeletedInsideTransaction { get; private set; }
        private int _entered;

        public bool TryEnter(DbContext? database, string action)
        {
            if (database is null || FormId is not { } id) return false;
            var matches = action == "submit"
                ? database.ChangeTracker.Entries<Response>().Any(entry => entry.Entity.FormId == id)
                : database.ChangeTracker.Entries<Form>().Any(entry => entry.Entity.Id == id && entry.Entity.DeletedAt is not null);
            return matches && Interlocked.CompareExchange(ref _entered, 1, 0) == 0;
        }

        public async Task FailAsync(DbConnection connection, DbTransaction transaction, DbContext database, string action, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = FormId!.Value;
            command.Parameters.Add(id);
            if (action == "submit")
            {
                ResponseId = database.ChangeTracker.Entries<Response>().Single(entry => entry.Entity.FormId == FormId).Entity.Id;
                command.CommandText = "SELECT COUNT(*) FROM responses WHERE form_id=@id";
                ResponsesBeforeFailure = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
                command.CommandText = "SELECT COUNT(*) FROM answers WHERE response_id IN (SELECT id FROM responses WHERE form_id=@id)";
                AnswersBeforeFailure = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            }
            else
            {
                command.CommandText = "SELECT deleted_at IS NOT NULL FROM forms WHERE id=@id";
                DeletedInsideTransaction = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            }
            Injected = true;
            command.CommandText = "SELECT 1 / 0"; // Actual PostgreSQL abort, before COMMIT has been sent.
            await command.ExecuteScalarAsync(cancellationToken);
        }
    }

    private sealed class R8AfterWriteFault(string action, R8FaultEvidence evidence) : DbCommandInterceptor
    {
        private bool Matches(DbCommand command) => command.CommandText.Contains(
            action == "submit" ? "INSERT INTO answers" : "UPDATE forms SET", StringComparison.Ordinal);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Matches(command) && evidence.TryEnter(eventData.Context, action))
            {
                // Finish this actual EF statement before probing and failing on the same connection/transaction.
                await result.DisposeAsync();
                await evidence.FailAsync(command.Connection!, command.Transaction!, eventData.Context!, action, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Matches(command) && evidence.TryEnter(eventData.Context, action))
                await evidence.FailAsync(command.Connection!, command.Transaction!, eventData.Context!, action, cancellationToken);
            return result;
        }
    }

    private sealed class R8BeforeCommitFault(string action, R8FaultEvidence evidence) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (evidence.TryEnter(eventData.Context, action))
                await evidence.FailAsync(transaction.Connection!, transaction, eventData.Context!, action, cancellationToken);
            return result;
        }
    }
}
