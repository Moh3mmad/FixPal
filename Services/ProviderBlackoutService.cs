using System.Data;
using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Services;

public sealed class ProviderBlackoutService(
    ApplicationDbContext db,
    RequestAccessService access,
    SchedulingTimePolicy timePolicy,
    TimeProvider timeProvider,
    ILogger<ProviderBlackoutService> logger)
{
    public async Task<ProviderBlackoutManagementReadModel?> GetOwnAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var provider = await ResolveActiveProviderAsync(user, ct);
        if (provider == null) return null;
        var calendarVersion = await db.ProviderCalendars.AsNoTracking()
            .Where(c => c.ProviderProfileId == provider.ProviderId)
            .Select(c => c.RowVersion).SingleOrDefaultAsync(ct);
        if (calendarVersion == null) return null;
        var blackouts = await db.ProviderBlackouts.AsNoTracking()
            .Where(b => b.ProviderProfileId == provider.ProviderId)
            .OrderByDescending(b => b.StartUtc).ThenByDescending(b => b.Id)
            .Select(b => new ProviderBlackoutReadModel(b.Id, b.StartUtc, b.EndUtc, b.CreatedAtUtc,
                b.CreatedByUserId, b.RemovedAtUtc, b.RemovedByUserId)).ToListAsync(ct);
        return new(Convert.ToBase64String(calendarVersion), blackouts.AsReadOnly());
    }

    public async Task<ProviderBlackoutResult> CreateAsync(ClaimsPrincipal user,
        CreateProviderBlackoutCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = await ResolveActiveProviderAsync(user, ct);
        if (provider == null) return Result(ProviderBlackoutStatus.Forbidden);
        EnsureIndependentWrite();
        ProviderBlackout? addedBlackout = null;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var currentProvider = await ResolveActiveProviderAsync(user, ct);
            if (currentProvider != provider) return Result(ProviderBlackoutStatus.Forbidden);

            var calendar = await LockCalendarAsync(provider.ProviderId, ct);
            if (calendar == null) return Result(ProviderBlackoutStatus.NotFound);
            var versionResult = ValidateVersion(command.ExpectedCalendarRowVersion, calendar.RowVersion);
            if (versionResult != null) return versionResult;

            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId); }
            catch (TimeZoneNotFoundException)
            { return Error(ProviderBlackoutStatus.Conflict, "TimeZoneId", "CalendarTimeZoneUnavailable"); }
            catch (InvalidTimeZoneException)
            { return Error(ProviderBlackoutStatus.Conflict, "TimeZoneId", "CalendarTimeZoneInvalid"); }

            var resolved = timePolicy.ResolveLocalInterval(command.StartLocal, command.EndLocal, zone);
            if (!resolved.IsSuccess)
                return Error(ProviderBlackoutStatus.ValidationFailed, TimeField(resolved.Error), TimeCode(resolved.Error));
            var interval = resolved.Interval!;
            if (!timePolicy.IsFutureStart(interval.StartUtc))
                return Error(ProviderBlackoutStatus.ValidationFailed, nameof(command.StartLocal), "StartMustBeFuture");

            var blackoutConflict = await db.ProviderBlackouts.AsNoTracking().AnyAsync(b =>
                b.ProviderProfileId == provider.ProviderId && b.RemovedAtUtc == null
                && b.StartUtc < interval.EndUtc && interval.StartUtc < b.EndUtc, ct);
            if (blackoutConflict)
                return Error(ProviderBlackoutStatus.Conflict, string.Empty, "BlackoutOverlap");

            var appointmentConflict = await db.Appointments.AsNoTracking().AnyAsync(a =>
                a.ProviderProfileId == provider.ProviderId
                && ((a.Status == AppointmentStatus.Scheduled
                        && a.StartUtc < interval.EndUtc && interval.StartUtc < a.EndUtc)
                    || (a.Status == AppointmentStatus.InProgress && a.StartUtc < interval.EndUtc)), ct);
            if (appointmentConflict)
                return Error(ProviderBlackoutStatus.Conflict, string.Empty, "AppointmentOverlap");

            var now = timeProvider.GetUtcNow();
            addedBlackout = new ProviderBlackout
            {
                ProviderProfileId = provider.ProviderId,
                StartUtc = interval.StartUtc,
                EndUtc = interval.EndUtc,
                CreatedAtUtc = now,
                CreatedByUserId = provider.UserId,
                RemovedAtUtc = null,
                RemovedByUserId = null
            };
            db.ProviderBlackouts.Add(addedBlackout);
            await db.SaveChangesAsync(ct);
            var changed = await AdvanceCalendarAsync(provider.ProviderId, calendar.RowVersion, now, ct);
            if (changed != 1)
                return Error(ProviderBlackoutStatus.Stale, nameof(command.ExpectedCalendarRowVersion), "CalendarChanged");
            await tx.CommitAsync(ct);
            return Result(ProviderBlackoutStatus.Success);
        }
        catch (Exception ex) when (IsExpectedConflict(ex))
        {
            logger.LogWarning("Concurrent blackout creation rejected for provider {ProviderId}", provider.ProviderId);
            return Result(ProviderBlackoutStatus.Conflict);
        }
        finally
        {
            if (addedBlackout != null) db.Entry(addedBlackout).State = EntityState.Detached;
        }
    }

    public async Task<ProviderBlackoutResult> RemoveAsync(ClaimsPrincipal user,
        RemoveProviderBlackoutCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BlackoutId <= 0)
            return Error(ProviderBlackoutStatus.ValidationFailed, nameof(command.BlackoutId), "InvalidBlackoutId");
        var provider = await ResolveActiveProviderAsync(user, ct);
        if (provider == null) return Result(ProviderBlackoutStatus.Forbidden);
        EnsureIndependentWrite();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var currentProvider = await ResolveActiveProviderAsync(user, ct);
            if (currentProvider != provider) return Result(ProviderBlackoutStatus.Forbidden);

            var calendar = await LockCalendarAsync(provider.ProviderId, ct);
            if (calendar == null) return Result(ProviderBlackoutStatus.NotFound);
            var versionResult = ValidateVersion(command.ExpectedCalendarRowVersion, calendar.RowVersion);
            if (versionResult != null) return versionResult;

            var blackout = await db.ProviderBlackouts.AsNoTracking()
                .Where(b => b.Id == command.BlackoutId && b.ProviderProfileId == provider.ProviderId)
                .Select(b => new { b.RemovedAtUtc }).SingleOrDefaultAsync(ct);
            if (blackout == null) return Result(ProviderBlackoutStatus.NotFound);
            if (blackout.RemovedAtUtc != null)
                return Error(ProviderBlackoutStatus.Conflict, nameof(command.BlackoutId), "BlackoutAlreadyRemoved");

            var now = timeProvider.GetUtcNow();
            var removed = await db.ProviderBlackouts
                .Where(b => b.Id == command.BlackoutId && b.ProviderProfileId == provider.ProviderId && b.RemovedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.RemovedAtUtc, now)
                    .SetProperty(b => b.RemovedByUserId, provider.UserId), ct);
            if (removed != 1)
                return Error(ProviderBlackoutStatus.Conflict, nameof(command.BlackoutId), "BlackoutAlreadyRemoved");
            var changed = await AdvanceCalendarAsync(provider.ProviderId, calendar.RowVersion, now, ct);
            if (changed != 1)
                return Error(ProviderBlackoutStatus.Stale, nameof(command.ExpectedCalendarRowVersion), "CalendarChanged");
            await tx.CommitAsync(ct);
            return Result(ProviderBlackoutStatus.Success);
        }
        catch (Exception ex) when (IsExpectedConflict(ex))
        {
            logger.LogWarning("Concurrent blackout removal rejected for provider {ProviderId}", provider.ProviderId);
            return Result(ProviderBlackoutStatus.Conflict);
        }
    }

    private async Task<ActiveProvider?> ResolveActiveProviderAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true) return null;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var providerId = await access.ProviderIdAsync(user, ct);
        if (providerId == null) return null;
        return await ProviderEligibility.Active(db).AsNoTracking()
            .AnyAsync(p => p.Id == providerId && p.UserId == userId, ct)
            ? new(providerId.Value, userId) : null;
    }

    private Task<ProviderCalendar?> LockCalendarAsync(int providerId, CancellationToken ct) =>
        db.ProviderCalendars.FromSqlInterpolated(
            $"SELECT * FROM [ProviderCalendars] WITH (UPDLOCK, HOLDLOCK) WHERE [ProviderProfileId] = {providerId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);

    private static ProviderBlackoutResult? ValidateVersion(string? encoded, byte[] current)
    {
        var expected = new byte[8];
        if (encoded == null || !Convert.TryFromBase64String(encoded, expected, out var written) || written != expected.Length)
            return Error(ProviderBlackoutStatus.ValidationFailed, "ExpectedCalendarRowVersion", "InvalidRowVersion");
        return current.AsSpan().SequenceEqual(expected)
            ? null : Error(ProviderBlackoutStatus.Stale, "ExpectedCalendarRowVersion", "CalendarChanged");
    }

    private Task<int> AdvanceCalendarAsync(int providerId, byte[] expectedVersion,
        DateTimeOffset updatedAtUtc, CancellationToken ct) =>
        db.ProviderCalendars.Where(c => c.ProviderProfileId == providerId && c.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAtUtc, updatedAtUtc), ct);

    private void EnsureIndependentWrite()
    {
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Blackout commands own their transaction.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Blackout commands require a context without pending changes.");
    }

    private static string TimeField(SchedulingTimeError error) => error switch
    {
        SchedulingTimeError.InvalidDateTimeKind => "StartLocal,EndLocal",
        SchedulingTimeError.MinutePrecisionRequired => "StartLocal,EndLocal",
        SchedulingTimeError.InvalidInterval => "EndLocal",
        SchedulingTimeError.NonexistentLocalTime => "StartLocal,EndLocal",
        SchedulingTimeError.AmbiguousLocalTime => "StartLocal,EndLocal",
        SchedulingTimeError.UtcConversionOutOfRange => "StartLocal,EndLocal",
        _ => string.Empty
    };

    private static string TimeCode(SchedulingTimeError error) => error switch
    {
        SchedulingTimeError.InvalidDateTimeKind => "InvalidDateTimeKind",
        SchedulingTimeError.MinutePrecisionRequired => "MinutePrecisionRequired",
        SchedulingTimeError.InvalidInterval => "InvalidInterval",
        SchedulingTimeError.NonexistentLocalTime => "NonexistentLocalTime",
        SchedulingTimeError.AmbiguousLocalTime => "AmbiguousLocalTime",
        SchedulingTimeError.UtcConversionOutOfRange => "UtcConversionOutOfRange",
        _ => "InvalidLocalInterval"
    };

    private static bool IsExpectedConflict(Exception ex) => ex is DbUpdateConcurrencyException
        || ex is SqlException { Number: 1205 or 1222 or 2601 or 2627 }
        || ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 or 2601 or 2627 } };

    private static ProviderBlackoutResult Result(ProviderBlackoutStatus status) =>
        new(status, Array.Empty<ProviderBlackoutError>());

    private static ProviderBlackoutResult Error(ProviderBlackoutStatus status, string field, string code) =>
        new(status, Array.AsReadOnly(new[] { new ProviderBlackoutError(field, code) }));

    private sealed record ActiveProvider(int ProviderId, string UserId);
}
