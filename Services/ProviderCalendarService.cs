using System.Data;
using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixPal.Services;

public sealed class ProviderCalendarService(
    ApplicationDbContext db,
    RequestAccessService access,
    SchedulingTimePolicy timePolicy,
    TimeProvider timeProvider,
    ILogger<ProviderCalendarService> logger)
{
    public async Task<ProviderCalendarReadModel?> GetOwnAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var providerId = await ResolveActiveProviderAsync(user, ct);
        if (providerId == null) return null;
        var calendar = await db.ProviderCalendars.AsNoTracking()
            .Where(c => c.ProviderProfileId == providerId)
            .Select(c => new
            {
                c.TimeZoneId, c.IsEnabled, c.UpdatedAtUtc, c.RowVersion,
                Periods = db.ProviderWorkingPeriods.AsNoTracking()
                    .Where(p => p.ProviderProfileId == c.ProviderProfileId)
                    .OrderBy(p => p.DayOfWeek).ThenBy(p => p.StartLocal).ThenBy(p => p.EndLocal)
                    .Select(p => new CalendarWorkingPeriod(p.DayOfWeek, p.StartLocal, p.EndLocal)).ToList()
            }).SingleOrDefaultAsync(ct);
        return calendar == null ? null : new(calendar.TimeZoneId, calendar.IsEnabled, calendar.UpdatedAtUtc,
            Convert.ToBase64String(calendar.RowVersion), calendar.Periods.AsReadOnly());
    }

    public async Task<ProviderCalendarResult> CreateAsync(ClaimsPrincipal user,
    CreateProviderCalendarCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var providerId = await ResolveActiveProviderAsync(user, ct);
        if (providerId == null) return Result(ProviderCalendarStatus.Forbidden);

        EnsureIndependentWrite();

        var submittedPeriods = command.WorkingPeriods?.ToArray();
        var strategy = db.Database.CreateExecutionStrategy();

        ProviderCalendar? addedCalendar = null;
        var addedPeriods = new List<ProviderWorkingPeriod>();

        var commitAttempted = false;
        string? attemptedTimeZoneId = null;
        var attemptedIsEnabled = false;
        DateTimeOffset attemptedUpdatedAtUtc = default;
        CalendarWorkingPeriod[] attemptedPeriods = Array.Empty<CalendarWorkingPeriod>();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();

                db.ChangeTracker.Clear();
                addedCalendar = null;
                addedPeriods.Clear();

                if (commitAttempted && attemptedTimeZoneId != null)
                {
                    if (await MatchesCommittedStateAsync(
                            providerId.Value,
                            attemptedTimeZoneId,
                            attemptedIsEnabled,
                            attemptedUpdatedAtUtc,
                            attemptedPeriods,
                            ct))
                    {
                        return Result(ProviderCalendarStatus.Success);
                    }

                    commitAttempted = false;
                }

                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);

                // The calendar may not exist yet. Its owning profile serializes initial creation.
                var profile = await db.ProviderProfiles.FromSqlInterpolated(
                        $"SELECT * FROM [ProviderProfiles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {providerId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);

                if (profile == null
                    || await ResolveActiveProviderAsync(user, ct) != providerId)
                {
                    return Result(ProviderCalendarStatus.Forbidden);
                }

                if (await db.ProviderCalendars.AsNoTracking()
                    .AnyAsync(c => c.ProviderProfileId == providerId.Value, ct))
                {
                    return Error(
                        ProviderCalendarStatus.Conflict,
                        string.Empty,
                        "CalendarAlreadyExists");
                }

                var errors = new List<ProviderCalendarError>();
                var zone = ResolveTimeZone(command.TimeZoneId, errors);
                var periods = ValidatePeriods(
                    submittedPeriods,
                    command.IsEnabled,
                    errors);

                if (errors.Count != 0)
                {
                    return new(
                        ProviderCalendarStatus.ValidationFailed,
                        errors.AsReadOnly());
                }

                var now = timeProvider.GetUtcNow();

                addedCalendar = new ProviderCalendar
                {
                    ProviderProfileId = providerId.Value,
                    TimeZoneId = zone!.Id,
                    IsEnabled = command.IsEnabled,
                    UpdatedAtUtc = now
                };

                db.ProviderCalendars.Add(addedCalendar);

                addedPeriods.AddRange(
                    ToEntities(providerId.Value, periods));

                db.ProviderWorkingPeriods.AddRange(addedPeriods);

                await db.SaveChangesAsync(ct);

                attemptedTimeZoneId = zone.Id;
                attemptedIsEnabled = command.IsEnabled;
                attemptedUpdatedAtUtc = now;
                attemptedPeriods = periods.ToArray();
                commitAttempted = true;

                await tx.CommitAsync(ct);

                return Result(ProviderCalendarStatus.Success);
            });
        }
        catch (Exception ex) when (IsExpectedConflict(ex))
        {
            db.ChangeTracker.Clear();

            logger.LogWarning(
                ex,
                "Concurrent calendar creation rejected for provider {ProviderId}",
                providerId);

            return Result(ProviderCalendarStatus.Conflict);
        }
        finally
        {
            foreach (var period in addedPeriods)
            {
                if (db.Entry(period).State != EntityState.Detached)
                    db.Entry(period).State = EntityState.Detached;
            }

            if (addedCalendar != null
                && db.Entry(addedCalendar).State != EntityState.Detached)
            {
                db.Entry(addedCalendar).State = EntityState.Detached;
            }
        }
    }

    public async Task<ProviderCalendarResult> UpdateAsync(ClaimsPrincipal user,
     UpdateProviderCalendarCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var providerId = await ResolveActiveProviderAsync(user, ct);
        if (providerId == null) return Result(ProviderCalendarStatus.Forbidden);

        EnsureIndependentWrite();

        var submittedPeriods = command.WorkingPeriods?.ToArray();
        var strategy = db.Database.CreateExecutionStrategy();
        var addedPeriods = new List<ProviderWorkingPeriod>();

        var commitAttempted = false;
        string? attemptedTimeZoneId = null;
        var attemptedIsEnabled = false;
        DateTimeOffset attemptedUpdatedAtUtc = default;
        CalendarWorkingPeriod[] attemptedPeriods = Array.Empty<CalendarWorkingPeriod>();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();

                db.ChangeTracker.Clear();
                addedPeriods.Clear();

                if (commitAttempted && attemptedTimeZoneId != null)
                {
                    if (await MatchesCommittedStateAsync(
                            providerId.Value,
                            attemptedTimeZoneId,
                            attemptedIsEnabled,
                            attemptedUpdatedAtUtc,
                            attemptedPeriods,
                            ct))
                    {
                        return Result(ProviderCalendarStatus.Success);
                    }

                    commitAttempted = false;
                }

                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);

                // Read provider eligibility before locking the calendar,
                // matching creation's resource order.
                if (await ResolveActiveProviderAsync(user, ct) != providerId)
                    return Result(ProviderCalendarStatus.Forbidden);

                var calendar = await db.ProviderCalendars.FromSqlInterpolated(
                        $"SELECT * FROM [ProviderCalendars] WITH (UPDLOCK, HOLDLOCK) WHERE [ProviderProfileId] = {providerId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);

                if (calendar == null)
                    return Result(ProviderCalendarStatus.NotFound);

                var expectedVersion = new byte[8];

                if (command.ExpectedRowVersion == null
                    || !Convert.TryFromBase64String(
                        command.ExpectedRowVersion,
                        expectedVersion,
                        out var written)
                    || written != expectedVersion.Length)
                {
                    return Error(
                        ProviderCalendarStatus.ValidationFailed,
                        nameof(command.ExpectedRowVersion),
                        "InvalidRowVersion");
                }

                if (!calendar.RowVersion.AsSpan().SequenceEqual(expectedVersion))
                {
                    return Error(
                        ProviderCalendarStatus.Stale,
                        nameof(command.ExpectedRowVersion),
                        "CalendarChanged");
                }

                var errors = new List<ProviderCalendarError>();
                var zone = ResolveTimeZone(command.TimeZoneId, errors);
                var periods = ValidatePeriods(
                    submittedPeriods,
                    command.IsEnabled,
                    errors);

                if (errors.Count != 0)
                {
                    return new(
                        ProviderCalendarStatus.ValidationFailed,
                        errors.AsReadOnly());
                }

                var active = db.Appointments.AsNoTracking()
                    .Where(a =>
                        a.ProviderProfileId == providerId.Value
                        && (a.Status == AppointmentStatus.Confirmed
                            || a.Status == AppointmentStatus.InProgress));

                if (!string.Equals(
                        calendar.TimeZoneId,
                        zone!.Id,
                        StringComparison.Ordinal)
                    && await active.AnyAsync(ct))
                {
                    return Error(
                        ProviderCalendarStatus.Conflict,
                        nameof(command.TimeZoneId),
                        "ActiveAppointmentsPreventTimeZoneChange");
                }

                var existingPeriods = await db.ProviderWorkingPeriods.AsNoTracking()
                    .Where(p => p.ProviderProfileId == providerId.Value)
                    .OrderBy(p => p.DayOfWeek)
                    .ThenBy(p => p.StartLocal)
                    .ThenBy(p => p.EndLocal)
                    .Select(p => new CalendarWorkingPeriod(
                        p.DayOfWeek,
                        p.StartLocal,
                        p.EndLocal))
                    .ToListAsync(ct);

                if (!existingPeriods.SequenceEqual(periods))
                {
                    var now = timeProvider.GetUtcNow();

                    var futureAppointments = await active
                        .Where(a =>
                            a.Status == AppointmentStatus.Confirmed
                            && a.StartUtc > now)
                        .Select(a => new
                        {
                            a.StartUtc,
                            a.EndUtc
                        })
                        .ToListAsync(ct);

                    var definitions = periods
                        .Select(p => new WorkingPeriodDefinition(
                            p.DayOfWeek,
                            p.StartLocal,
                            p.EndLocal))
                        .ToArray();

                    if (futureAppointments.Any(a =>
                            !timePolicy.FitsWorkingPeriod(
                                new UtcInterval(a.StartUtc, a.EndUtc),
                                zone,
                                definitions)))
                    {
                        return Error(
                            ProviderCalendarStatus.Conflict,
                            nameof(command.WorkingPeriods),
                            "WorkingPeriodsInvalidateAppointment");
                    }
                }

                await db.ProviderWorkingPeriods
                    .Where(p => p.ProviderProfileId == providerId.Value)
                    .ExecuteDeleteAsync(ct);

                addedPeriods.AddRange(
                    ToEntities(providerId.Value, periods));

                db.ProviderWorkingPeriods.AddRange(addedPeriods);

                await db.SaveChangesAsync(ct);

                var updatedAtUtc = timeProvider.GetUtcNow();

                var changed = await db.ProviderCalendars
                    .Where(c =>
                        c.ProviderProfileId == providerId.Value
                        && c.RowVersion == expectedVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.TimeZoneId, zone.Id)
                        .SetProperty(c => c.IsEnabled, command.IsEnabled)
                        .SetProperty(c => c.UpdatedAtUtc, updatedAtUtc), ct);

                if (changed != 1)
                {
                    return Error(
                        ProviderCalendarStatus.Stale,
                        nameof(command.ExpectedRowVersion),
                        "CalendarChanged");
                }

                attemptedTimeZoneId = zone.Id;
                attemptedIsEnabled = command.IsEnabled;
                attemptedUpdatedAtUtc = updatedAtUtc;
                attemptedPeriods = periods.ToArray();
                commitAttempted = true;

                await tx.CommitAsync(ct);

                return Result(ProviderCalendarStatus.Success);
            });
        }
        catch (Exception ex) when (IsExpectedConflict(ex))
        {
            db.ChangeTracker.Clear();

            logger.LogWarning(
                ex,
                "Concurrent calendar update rejected for provider {ProviderId}",
                providerId);

            return Result(ProviderCalendarStatus.Conflict);
        }
        finally
        {
            foreach (var period in addedPeriods)
            {
                if (db.Entry(period).State != EntityState.Detached)
                    db.Entry(period).State = EntityState.Detached;
            }
        }
    }

    private async Task<int?> ResolveActiveProviderAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true) return null;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var providerId = await access.ProviderIdAsync(user, ct);
        if (providerId == null) return null;
        return await ProviderEligibility.Active(db).AsNoTracking()
            .AnyAsync(p => p.Id == providerId && p.UserId == userId, ct) ? providerId : null;
    }

    private void EnsureIndependentWrite()
    {
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Calendar configuration commands own their transaction.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Calendar configuration commands require a context without pending changes.");
    }

    private static TimeZoneInfo? ResolveTimeZone(string? submittedId, List<ProviderCalendarError> errors)
    {
        var id = submittedId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 100)
        {
            errors.Add(new("TimeZoneId", "InvalidTimeZoneId"));
            return null;
        }
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            if (zone.Id.Length <= 100) return zone;
            errors.Add(new("TimeZoneId", "InvalidTimeZoneId"));
        }
        catch (TimeZoneNotFoundException) { errors.Add(new("TimeZoneId", "UnknownTimeZone")); }
        catch (InvalidTimeZoneException) { errors.Add(new("TimeZoneId", "InvalidTimeZone")); }
        return null;
    }

    private static CalendarWorkingPeriod[] ValidatePeriods(IReadOnlyList<CalendarWorkingPeriod>? submitted,
        bool enabled, List<ProviderCalendarError> errors)
    {
        if (submitted == null)
        {
            errors.Add(new("WorkingPeriods", "WorkingPeriodsRequired"));
            return Array.Empty<CalendarWorkingPeriod>();
        }
        if (enabled && submitted.Count == 0) errors.Add(new("WorkingPeriods", "EnabledCalendarRequiresWorkingPeriod"));
        var valid = new List<CalendarWorkingPeriod>();
        for (var i = 0; i < submitted.Count; i++)
        {
            var period = submitted[i];
            var field = $"WorkingPeriods[{i}]";
            if (period == null) { errors.Add(new(field, "WorkingPeriodRequired")); continue; }
            if (!Enum.IsDefined(period.DayOfWeek)) { errors.Add(new(field + ".DayOfWeek", "InvalidDayOfWeek")); continue; }
            if (period.StartLocal >= period.EndLocal) { errors.Add(new(field, "InvalidWorkingInterval")); continue; }
            if (period.StartLocal.Ticks % TimeSpan.TicksPerMinute != 0 || period.EndLocal.Ticks % TimeSpan.TicksPerMinute != 0)
            { errors.Add(new(field, "MinutePrecisionRequired")); continue; }
            valid.Add(period);
        }
        var ordered = valid.OrderBy(p => p.DayOfWeek).ThenBy(p => p.StartLocal).ThenBy(p => p.EndLocal).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if (previous == current) errors.Add(new("WorkingPeriods", "DuplicateWorkingPeriod"));
            else if (previous.DayOfWeek == current.DayOfWeek && current.StartLocal < previous.EndLocal)
                errors.Add(new("WorkingPeriods", "OverlappingWorkingPeriods"));
        }
        return ordered;
    }

    private static IEnumerable<ProviderWorkingPeriod> ToEntities(int providerId, IEnumerable<CalendarWorkingPeriod> periods) =>
        periods.Select(p => new ProviderWorkingPeriod
        {
            ProviderProfileId = providerId, DayOfWeek = p.DayOfWeek, StartLocal = p.StartLocal, EndLocal = p.EndLocal
        });
    private async Task<bool> MatchesCommittedStateAsync(
    int providerId,
    string timeZoneId,
    bool isEnabled,
    DateTimeOffset updatedAtUtc,
    IReadOnlyList<CalendarWorkingPeriod> expectedPeriods,
    CancellationToken ct)
    {
        var calendarMatches = await db.ProviderCalendars.AsNoTracking()
            .AnyAsync(c =>
                c.ProviderProfileId == providerId
                && c.TimeZoneId == timeZoneId
                && c.IsEnabled == isEnabled
                && c.UpdatedAtUtc == updatedAtUtc, ct);

        if (!calendarMatches)
            return false;

        var currentPeriods = await db.ProviderWorkingPeriods.AsNoTracking()
            .Where(p => p.ProviderProfileId == providerId)
            .OrderBy(p => p.DayOfWeek)
            .ThenBy(p => p.StartLocal)
            .ThenBy(p => p.EndLocal)
            .Select(p => new CalendarWorkingPeriod(
                p.DayOfWeek,
                p.StartLocal,
                p.EndLocal))
            .ToListAsync(ct);

        return currentPeriods.SequenceEqual(expectedPeriods);
    }
    private static bool IsExpectedConflict(Exception ex) => ex is DbUpdateConcurrencyException
        || ex is SqlException { Number: 1205 or 1222 or 2601 or 2627 }
        || ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 or 2601 or 2627 } };

    private static ProviderCalendarResult Result(ProviderCalendarStatus status) => new(status, Array.Empty<ProviderCalendarError>());

    private static ProviderCalendarResult Error(ProviderCalendarStatus status, string field, string code) =>
        new(status, Array.AsReadOnly(new[] { new ProviderCalendarError(field, code) }));
}
