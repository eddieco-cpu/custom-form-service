using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Infrastructure.Persistence;

/// <summary>
/// Persists PATCH option changes without exposing PostgreSQL's immediate unique constraint
/// on (question_id, value) to legitimate final-state transitions such as value swaps.
/// Both saves run in the caller's Form aggregate transaction.
/// </summary>
public static class OptionValueTransitionWriter
{
    public static async Task SaveAsync(
        ApplicationDbContext database,
        IReadOnlyList<Option> deferredOptions,
        CancellationToken cancellationToken)
    {
        var autoDetectChanges = database.ChangeTracker.AutoDetectChangesEnabled;
        database.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var addedOptions = database.ChangeTracker.Entries<Option>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToArray();
            var hasDeletedOptions = database.ChangeTracker.Entries<Option>()
                .Any(entry => entry.State == EntityState.Deleted);

            if (deferredOptions.Count == 0 && (addedOptions.Length == 0 || !hasDeletedOptions))
            {
                await database.SaveChangesAsync(cancellationToken);
                return;
            }

            // EF models (question_id, value) as an alternate key, so changing Value on
            // a tracked Option is forbidden even before the database unique constraint
            // can be evaluated. Delta application therefore deletes the old row and
            // keeps its same-ID replacement detached until that delete is persisted.
            // New Options also stay detached until their normalized candidate is valid
            // and outgoing alternate keys have been removed.
            foreach (var option in deferredOptions)
            {
                option.Question.Options.Remove(option);
            }

            // A new value can reuse the value of a deleted option in this same PATCH.
            // Keep all additions out of the first save so provider command order is
            // irrelevant; they are reattached after the old rows have been deleted.
            foreach (var option in addedOptions)
            {
                option.Question.Options.Remove(option);
                database.Entry(option).State = EntityState.Detached;
            }

            await database.SaveChangesAsync(cancellationToken);

            foreach (var option in deferredOptions)
            {
                option.Question.Options.Add(option);
                database.Options.Add(option);
            }

            foreach (var option in addedOptions)
            {
                option.Question.Options.Add(option);
                database.Options.Add(option);
            }

            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }
}
