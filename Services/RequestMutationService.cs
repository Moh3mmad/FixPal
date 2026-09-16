using System.Data;
using FixPal.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;

public enum MutationResult { Success, NotFound, Conflict }

// All request-related writes acquire the same SQL row lock first. This prevents
// accept/revise/start/complete races without distributed locks or broad table locks.
public class RequestMutationService(ApplicationDbContext db, ILogger<RequestMutationService> logger)
{
    public async Task<MutationResult> RunAsync(int id, Func<Task<MutationResult>> action, CancellationToken ct)
    {
        if (id <= 0) return MutationResult.NotFound;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var exists = await db.MaintenanceRequests.FromSqlInterpolated($"SELECT * FROM [MaintenanceRequests] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .AsNoTracking().AnyAsync(ct);
            if (!exists) return MutationResult.NotFound;
            var result = await action();
            if (result == MutationResult.Success) await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException
            || ex is SqlException { Number: 1205 or 1222 or 2601 or 2627 }
            || ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 or 2601 or 2627 } })
        {
            db.ChangeTracker.Clear();
            logger.LogWarning("Concurrent request mutation rejected for {RequestId}", id);
            return MutationResult.Conflict;
        }
    }
}
