using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CustomFormService.Api.Infrastructure.Persistence;

public static class FormAggregateLock
{
    // Every Form-definition writer must acquire this lock before reading the aggregate.
    // R7 Publish will use the same lock to serialize its state transition with PATCH.
    public static async Task<bool> TryAcquireActiveFormAsync(
        ApplicationDbContext database,
        Guid formId,
        CancellationToken cancellationToken)
    {
        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A transaction is required before locking a form aggregate.");
        var connection = database.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SELECT 1
            FROM forms
            WHERE id = @formId AND deleted_at IS NULL
            FOR UPDATE;
            """;
        var formIdParameter = command.CreateParameter();
        formIdParameter.ParameterName = "formId";
        formIdParameter.Value = formId;
        command.Parameters.Add(formIdParameter);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
