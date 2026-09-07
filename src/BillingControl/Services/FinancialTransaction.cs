using System.Data;
using BillingControl.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BillingControl.Services;

public static class FinancialTransaction
{
    public const string ConflictMessage = "Another financial transaction completed first. Refresh the page and try again.";

    public static async Task<T> Serializable<T>(AppDbContext db, Func<Task<T>> action)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await action();
            await transaction.CommitAsync();
            return result;
        }
        catch (Exception ex) when (IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(ConflictMessage, ex);
        }
    }

    public static Task Serializable(AppDbContext db, Func<Task> action) =>
        Serializable(db, async () => { await action(); return true; });

    public static bool IsSerializationConflict(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
