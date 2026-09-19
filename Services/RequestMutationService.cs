using System.Data;
using FixPal.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixPal.Services;

public enum MutationResult { Success, NotFound, Conflict }

// All request-related writes acquire the same SQL row lock first. This prevents
// accept/revise/start/complete races without distributed locks or broad table locks.
public class RequestMutationService(
    ApplicationDbContext db,
    ILogger<RequestMutationService> logger)
{
    public Task<MutationResult> RunAsync(
        int id,
        Func<Task<MutationResult>> action,
        CancellationToken ct) =>
        RunAsync(id, action, verifyCommitted: null, ct);

    public async Task<MutationResult> RunAsync(
        int id,
        Func<Task<MutationResult>> action,
        Func<CancellationToken, Task<bool>>? verifyCommitted,
        CancellationToken ct)
    {
        if (id <= 0)
            return MutationResult.NotFound;

        ArgumentNullException.ThrowIfNull(action);

        var strategy = db.Database.CreateExecutionStrategy();

        // False means it is still safe to replay the transactional callback.
        // Once CommitAsync has started, replaying arbitrary callbacks could
        // duplicate messages, reviews, quote revisions, appointments, etc.
        var commitAttempted = false;

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();

                db.ChangeTracker.Clear();

                // We are here because the previous CommitAsync produced a
                // transient/unknown outcome. Do not blindly execute action()
                // again.
                if (commitAttempted)
                {
                    if (verifyCommitted != null
                        && await verifyCommitted(ct))
                    {
                        return MutationResult.Success;
                    }

                    logger.LogWarning(
                        "Request mutation commit outcome could not be confirmed for {RequestId}; callback will not be replayed.",
                        id);

                    return MutationResult.Conflict;
                }

                await using var tx =
                    await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        ct);

                var exists = await db.MaintenanceRequests
                    .FromSqlInterpolated(
                        $"SELECT * FROM [MaintenanceRequests] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                    .AsNoTracking()
                    .AnyAsync(ct);

                if (!exists)
                    return MutationResult.NotFound;

                var result = await action();

                if (result != MutationResult.Success)
                    return result;

                // Every SQL operation inside action() has succeeded.
                // From this point on, a connection failure can leave the
                // final commit outcome unknown, so retries must verify or
                // return Conflict rather than replaying action().
                commitAttempted = true;

                await tx.CommitAsync(ct);

                return MutationResult.Success;
            });
        }
        catch (Exception ex) when (
            IsExpectedConflict(ex)
            || ex is RetryLimitExceededException)
        {
            db.ChangeTracker.Clear();

            logger.LogWarning(
                ex,
                "Concurrent or transient request mutation rejected for {RequestId}",
                id);

            return MutationResult.Conflict;
        }
    }

    private static bool IsExpectedConflict(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is SqlException
        {
            Number: 1205 or 1222 or 2601 or 2627
        }
        || ex is DbUpdateException
        {
            InnerException: SqlException
            {
                Number: 1205 or 1222 or 2601 or 2627
            }
        };
}