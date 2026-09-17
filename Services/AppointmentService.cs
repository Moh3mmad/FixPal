using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Services;

public sealed class AppointmentService(
    ApplicationDbContext db,
    RequestAccessService access,
    RequestAgreementPolicy agreement,
    RequestMutationService mutations,
    SchedulingTimePolicy timePolicy,
    TimeProvider clock)
{
    public async Task<AppointmentResult> ScheduleAsync(
        ClaimsPrincipal user,
        ScheduleAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(command);

        if (command.MaintenanceRequestId <= 0)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(command.MaintenanceRequestId), "InvalidRequestId");
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.FindFirstValue(ClaimTypes.NameIdentifier)))
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Scheduling requires RequestMutationService to own an independent transaction.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Scheduling requires a context without pending changes.");

        AppointmentResult? callbackResult = null;
        var outcome = await mutations.RunAsync(command.MaintenanceRequestId, async () =>
        {
            // RequestMutationService already holds the MaintenanceRequest row lock.
            callbackResult = await ScheduleLockedAsync(user, command, ct);
            return callbackResult.Status switch
            {
                AppointmentResultStatus.Success => MutationResult.Success,
                AppointmentResultStatus.NotFound => MutationResult.NotFound,
                _ => MutationResult.Conflict
            };
        }, ct);

        return outcome switch
        {
            MutationResult.Success when callbackResult is { Status: AppointmentResultStatus.Success } => callbackResult,
            MutationResult.NotFound => callbackResult is { Status: AppointmentResultStatus.NotFound }
                ? callbackResult
                : Error(AppointmentResultStatus.NotFound, nameof(command.MaintenanceRequestId), "RequestNotFound"),
            MutationResult.Conflict when callbackResult is { Status: not AppointmentResultStatus.Success } => callbackResult,
            _ => Error(AppointmentResultStatus.Conflict, string.Empty, "ConcurrentSchedulingConflict")
        };
    }

    public async Task<AppointmentResult> RescheduleAsync(
        ClaimsPrincipal user,
        RescheduleAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateMutationInput(command.MaintenanceRequestId, command.AppointmentId,
            command.ExpectedAppointmentRowVersion, out var expectedVersion);
        if (validation != null) return validation;
        var actorId = AuthenticatedActorId(user);
        if (actorId == null)
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        EnsureIndependentMutation();

        AppointmentResult? callbackResult = null;
        var outcome = await mutations.RunAsync(command.MaintenanceRequestId, async () =>
        {
            callbackResult = await RescheduleLockedAsync(user, actorId, command, expectedVersion, ct);
            return ToMutationResult(callbackResult);
        }, ct);
        return FinalResult(outcome, callbackResult);
    }

    public async Task<AppointmentResult> CancelAsync(
        ClaimsPrincipal user,
        CancelAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateMutationInput(command.MaintenanceRequestId, command.AppointmentId,
            command.ExpectedAppointmentRowVersion, out var expectedVersion);
        if (validation != null) return validation;
        var actorId = AuthenticatedActorId(user);
        if (actorId == null)
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        EnsureIndependentMutation();

        AppointmentResult? callbackResult = null;
        var outcome = await mutations.RunAsync(command.MaintenanceRequestId, async () =>
        {
            callbackResult = await CancelLockedAsync(user, actorId, command, expectedVersion, ct);
            return ToMutationResult(callbackResult);
        }, ct);
        return FinalResult(outcome, callbackResult);
    }

    private async Task<AppointmentResult> RescheduleLockedAsync(
        ClaimsPrincipal user,
        string actorId,
        RescheduleAppointmentCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var grant = await access.GetAsync(user, command.MaintenanceRequestId, ct);
        if (grant is not { CanParticipate: true } || (grant.IsOwner && grant.IsProvider))
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");

        var request = grant.Request;
        if (request.RequestType != RequestType.PrivateService || request.ProviderProfileId == null
            || request.Status != MaintenanceRequestStatus.Accepted)
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "RequestNotSchedulable");
        if (!await agreement.AgreedRequests.AnyAsync(r => r.Id == request.Id, ct))
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "AgreementRequired");

        var providerId = request.ProviderProfileId.Value;
        if (!await ProviderEligibility.ForRequest(db, request.ServiceCategoryId, request.AreaId, request.CustomerId)
            .AnyAsync(p => p.Id == providerId, ct))
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "ProviderNotEligible");

        var calendar = await LockCalendarAsync(providerId, ct);
        if (calendar == null)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarMissing");
        if (!calendar.IsEnabled)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarDisabled");
        var zone = ResolveTimeZone(calendar.TimeZoneId);
        if (zone == null)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarTimeZoneUnavailable");

        var current = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == command.AppointmentId && a.MaintenanceRequestId == request.Id
            && a.ProviderProfileId == providerId, ct);
        if (current == null)
            return Error(AppointmentResultStatus.NotFound, nameof(command.AppointmentId), "AppointmentNotFound");
        if (current.Status != AppointmentStatus.Scheduled)
            return Error(AppointmentResultStatus.Conflict, nameof(command.AppointmentId), "AppointmentNotReschedulable");
        if (!current.RowVersion.AsSpan().SequenceEqual(expectedVersion))
            return Error(AppointmentResultStatus.Conflict, nameof(command.ExpectedAppointmentRowVersion), "AppointmentChanged");

        var resolved = timePolicy.ResolveLocalInterval(command.StartLocal, command.EndLocal, zone);
        if (resolved.Interval == null)
            return TimeFailure(resolved.Error);
        var interval = resolved.Interval;
        if (!timePolicy.IsFutureStart(interval.StartUtc))
            return Error(AppointmentResultStatus.ValidationFailed, nameof(command.StartLocal), "StartMustBeFuture");

        var periods = await WorkingPeriodsAsync(providerId, ct);
        if (!timePolicy.FitsWorkingPeriod(interval, zone, periods))
            return IntervalError(AppointmentResultStatus.ValidationFailed, "OutsideWorkingHours");
        if (await HasBlackoutConflictAsync(providerId, interval, ct))
            return IntervalError(AppointmentResultStatus.Conflict, "BlackoutConflict");
        if (await db.Appointments.AsNoTracking().AnyAsync(a => a.ProviderProfileId == providerId && a.Id != current.Id
            && ((a.Status == AppointmentStatus.Scheduled && a.StartUtc < interval.EndUtc && interval.StartUtc < a.EndUtc)
                || (a.Status == AppointmentStatus.InProgress && a.StartUtc < interval.EndUtc)), ct))
            return IntervalError(AppointmentResultStatus.Conflict, "ProviderTimeConflict");

        var operationTime = clock.GetUtcNow();
        var changed = await db.Appointments.Where(a => a.Id == current.Id
                && a.MaintenanceRequestId == request.Id && a.ProviderProfileId == providerId
                && a.Status == AppointmentStatus.Scheduled && a.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Status, AppointmentStatus.Superseded)
                .SetProperty(a => a.ClosedAtUtc, operationTime)
                .SetProperty(a => a.ClosedByUserId, actorId), ct);
        if (changed != 1)
            return Error(AppointmentResultStatus.Conflict, nameof(command.ExpectedAppointmentRowVersion), "AppointmentChanged");

        var replacement = new Appointment
        {
            MaintenanceRequestId = request.Id,
            ProviderProfileId = providerId,
            StartUtc = interval.StartUtc,
            EndUtc = interval.EndUtc,
            TimeZoneId = calendar.TimeZoneId,
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = operationTime,
            CreatedByUserId = actorId,
            ClosedAtUtc = null,
            ClosedByUserId = null,
            ReplacesAppointmentId = current.Id
        };
        try
        {
            db.Appointments.Add(replacement);
            await db.SaveChangesAsync(ct);
            return Success();
        }
        finally
        {
            db.Entry(replacement).State = EntityState.Detached;
        }
    }

    private async Task<AppointmentResult> CancelLockedAsync(
        ClaimsPrincipal user,
        string actorId,
        CancelAppointmentCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var grant = await access.GetAsync(user, command.MaintenanceRequestId, ct);
        // RequestAccess handles the normal owner/approved-provider path. The direct
        // assignment read keeps cancellation available when an assigned provider
        // later ceases to satisfy current provider eligibility.
        var request = grant?.Request ?? await db.MaintenanceRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == command.MaintenanceRequestId, ct);
        if (request == null)
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        var assignedProviderUserId = request.ProviderProfileId == null
            ? null
            : await db.ProviderProfiles.AsNoTracking()
                .Where(p => p.Id == request.ProviderProfileId.Value)
                .Select(p => p.UserId)
                .SingleOrDefaultAsync(ct);
        var isOwner = request.CustomerId == actorId;
        var isAssignedProvider = assignedProviderUserId == actorId;
        if (!(isOwner || isAssignedProvider) || (isOwner && isAssignedProvider))
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");

        if (request.RequestType != RequestType.PrivateService || request.ProviderProfileId == null
            || request.Status != MaintenanceRequestStatus.Accepted)
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "RequestNotSchedulable");

        var providerId = request.ProviderProfileId.Value;
        var calendar = await LockCalendarAsync(providerId, ct);
        if (calendar == null)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarMissing");

        var current = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == command.AppointmentId && a.MaintenanceRequestId == request.Id
            && a.ProviderProfileId == providerId, ct);
        if (current == null)
            return Error(AppointmentResultStatus.NotFound, nameof(command.AppointmentId), "AppointmentNotFound");
        if (current.Status != AppointmentStatus.Scheduled)
            return Error(AppointmentResultStatus.Conflict, nameof(command.AppointmentId), "AppointmentNotCancellable");
        if (!current.RowVersion.AsSpan().SequenceEqual(expectedVersion))
            return Error(AppointmentResultStatus.Conflict, nameof(command.ExpectedAppointmentRowVersion), "AppointmentChanged");

        var operationTime = clock.GetUtcNow();
        var changed = await db.Appointments.Where(a => a.Id == current.Id
                && a.MaintenanceRequestId == request.Id && a.ProviderProfileId == providerId
                && a.Status == AppointmentStatus.Scheduled && a.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Status, AppointmentStatus.Cancelled)
                .SetProperty(a => a.ClosedAtUtc, operationTime)
                .SetProperty(a => a.ClosedByUserId, actorId), ct);
        return changed == 1
            ? Success()
            : Error(AppointmentResultStatus.Conflict, nameof(command.ExpectedAppointmentRowVersion), "AppointmentChanged");
    }

    private async Task<AppointmentResult> ScheduleLockedAsync(
        ClaimsPrincipal user,
        ScheduleAppointmentCommand command,
        CancellationToken ct)
    {
        var grant = await access.GetAsync(user, command.MaintenanceRequestId, ct);
        // The request exists under the outer lock; absent access means the actor is not allowed.
        if (grant is not { CanParticipate: true } || (grant.IsOwner && grant.IsProvider))
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");

        var request = grant.Request;
        if (request.RequestType != RequestType.PrivateService || request.ProviderProfileId == null
            || request.Status != MaintenanceRequestStatus.Accepted)
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "RequestNotSchedulable");
        if (!await agreement.AgreedRequests.AnyAsync(r => r.Id == request.Id, ct))
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "AgreementRequired");

        var providerId = request.ProviderProfileId.Value;
        if (!await ProviderEligibility.ForRequest(db, request.ServiceCategoryId, request.AreaId, request.CustomerId)
            .AnyAsync(p => p.Id == providerId, ct))
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "ProviderNotEligible");

        // All authorization, agreement and provider reads precede the calendar lock.
        // No request or provider lookup follows this point.
        var calendar = await db.ProviderCalendars.FromSqlInterpolated(
            $"SELECT * FROM [ProviderCalendars] WITH (UPDLOCK, HOLDLOCK) WHERE [ProviderProfileId] = {providerId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (calendar == null)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarMissing");
        if (!calendar.IsEnabled)
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarDisabled");

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarTimeZoneUnavailable");
        }
        catch (InvalidTimeZoneException)
        {
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarTimeZoneUnavailable");
        }
        catch (ArgumentException)
        {
            return Error(AppointmentResultStatus.Conflict, string.Empty, "CalendarTimeZoneUnavailable");
        }

        var resolved = timePolicy.ResolveLocalInterval(command.StartLocal, command.EndLocal, zone);
        if (resolved.Interval == null)
            return TimeFailure(resolved.Error);
        var interval = resolved.Interval;
        if (!timePolicy.IsFutureStart(interval.StartUtc))
            return Error(AppointmentResultStatus.ValidationFailed, nameof(command.StartLocal), "StartMustBeFuture");

        var periods = await db.ProviderWorkingPeriods.AsNoTracking()
            .Where(p => p.ProviderProfileId == providerId)
            .OrderBy(p => p.DayOfWeek).ThenBy(p => p.StartLocal).ThenBy(p => p.EndLocal)
            .Select(p => new WorkingPeriodDefinition(p.DayOfWeek, p.StartLocal, p.EndLocal))
            .ToListAsync(ct);
        if (!timePolicy.FitsWorkingPeriod(interval, zone, periods))
            return IntervalError(AppointmentResultStatus.ValidationFailed, "OutsideWorkingHours");

        if (await db.ProviderBlackouts.AsNoTracking().AnyAsync(b => b.ProviderProfileId == providerId
            && b.RemovedAtUtc == null && b.StartUtc < interval.EndUtc && interval.StartUtc < b.EndUtc, ct))
            return IntervalError(AppointmentResultStatus.Conflict, "BlackoutConflict");

        if (await db.Appointments.AsNoTracking().AnyAsync(a => a.MaintenanceRequestId == request.Id
            && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.InProgress), ct))
            return Error(AppointmentResultStatus.Conflict, nameof(command.MaintenanceRequestId), "RequestAlreadyScheduled");

        // Half-open occupancy permits adjacency; work in progress has no known end.
        if (await db.Appointments.AsNoTracking().AnyAsync(a => a.ProviderProfileId == providerId
            && ((a.Status == AppointmentStatus.Scheduled && a.StartUtc < interval.EndUtc && interval.StartUtc < a.EndUtc)
                || (a.Status == AppointmentStatus.InProgress && a.StartUtc < interval.EndUtc)), ct))
            return IntervalError(AppointmentResultStatus.Conflict, "ProviderTimeConflict");

        var appointment = new Appointment
        {
            MaintenanceRequestId = request.Id,
            ProviderProfileId = providerId,
            StartUtc = interval.StartUtc,
            EndUtc = interval.EndUtc,
            TimeZoneId = calendar.TimeZoneId,
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = clock.GetUtcNow(),
            CreatedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)!,
            ClosedAtUtc = null,
            ClosedByUserId = null,
            ReplacesAppointmentId = null
        };
        try
        {
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(ct);
            return new(AppointmentResultStatus.Success, Array.Empty<AppointmentError>());
        }
        finally
        {
            // Detach only this insert, including when SaveChanges or the outer commit fails.
            db.Entry(appointment).State = EntityState.Detached;
        }
    }

    private static AppointmentResult? ValidateMutationInput(
        int requestId,
        int appointmentId,
        string? encodedRowVersion,
        out byte[] expectedVersion)
    {
        expectedVersion = new byte[8];
        if (requestId <= 0)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(ScheduleAppointmentCommand.MaintenanceRequestId), "InvalidRequestId");
        if (appointmentId <= 0)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(RescheduleAppointmentCommand.AppointmentId), "InvalidAppointmentId");
        if (encodedRowVersion == null
            || !Convert.TryFromBase64String(encodedRowVersion, expectedVersion, out var bytesWritten)
            || bytesWritten != expectedVersion.Length)
            return Error(AppointmentResultStatus.ValidationFailed,
                nameof(RescheduleAppointmentCommand.ExpectedAppointmentRowVersion), "InvalidRowVersion");
        return null;
    }

    private static string? AuthenticatedActorId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    private void EnsureIndependentMutation()
    {
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Appointment mutations require RequestMutationService to own an independent transaction.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Appointment mutations require a context without pending changes.");
    }

    private static MutationResult ToMutationResult(AppointmentResult result) => result.Status switch
    {
        AppointmentResultStatus.Success => MutationResult.Success,
        AppointmentResultStatus.NotFound => MutationResult.NotFound,
        _ => MutationResult.Conflict
    };

    private static AppointmentResult FinalResult(
        MutationResult outcome,
        AppointmentResult? callbackResult) => outcome switch
    {
        MutationResult.Success when callbackResult is { Status: AppointmentResultStatus.Success } => callbackResult,
        MutationResult.NotFound => callbackResult is { Status: AppointmentResultStatus.NotFound }
            ? callbackResult
            : Error(AppointmentResultStatus.NotFound, nameof(ScheduleAppointmentCommand.MaintenanceRequestId), "RequestNotFound"),
        MutationResult.Conflict when callbackResult is { Status: not AppointmentResultStatus.Success } => callbackResult,
        _ => Error(AppointmentResultStatus.Conflict, string.Empty, "ConcurrentSchedulingConflict")
    };

    private Task<ProviderCalendar?> LockCalendarAsync(int providerId, CancellationToken ct) =>
        db.ProviderCalendars.FromSqlInterpolated(
            $"SELECT * FROM [ProviderCalendars] WITH (UPDLOCK, HOLDLOCK) WHERE [ProviderProfileId] = {providerId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);

    private static TimeZoneInfo? ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private Task<List<WorkingPeriodDefinition>> WorkingPeriodsAsync(int providerId, CancellationToken ct) =>
        db.ProviderWorkingPeriods.AsNoTracking()
            .Where(p => p.ProviderProfileId == providerId)
            .OrderBy(p => p.DayOfWeek).ThenBy(p => p.StartLocal).ThenBy(p => p.EndLocal)
            .Select(p => new WorkingPeriodDefinition(p.DayOfWeek, p.StartLocal, p.EndLocal))
            .ToListAsync(ct);

    private Task<bool> HasBlackoutConflictAsync(int providerId, UtcInterval interval, CancellationToken ct) =>
        db.ProviderBlackouts.AsNoTracking().AnyAsync(b => b.ProviderProfileId == providerId
            && b.RemovedAtUtc == null && b.StartUtc < interval.EndUtc && interval.StartUtc < b.EndUtc, ct);

    private static AppointmentResult Success() =>
        new(AppointmentResultStatus.Success, Array.Empty<AppointmentError>());

    private static AppointmentResult TimeFailure(SchedulingTimeError error)
    {
        var code = error switch
        {
            SchedulingTimeError.InvalidDateTimeKind => "InvalidDateTimeKind",
            SchedulingTimeError.MinutePrecisionRequired => "MinutePrecisionRequired",
            SchedulingTimeError.InvalidInterval => "InvalidInterval",
            SchedulingTimeError.NonexistentLocalTime => "NonexistentLocalTime",
            SchedulingTimeError.AmbiguousLocalTime => "AmbiguousLocalTime",
            SchedulingTimeError.UtcConversionOutOfRange => "UtcConversionOutOfRange",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unexpected scheduling time error.")
        };
        return error == SchedulingTimeError.InvalidInterval
            ? Error(AppointmentResultStatus.ValidationFailed, nameof(ScheduleAppointmentCommand.EndLocal), code)
            : IntervalError(AppointmentResultStatus.ValidationFailed, code);
    }

    private static AppointmentResult IntervalError(AppointmentResultStatus status, string code) =>
        new(status, Array.AsReadOnly(new[]
        {
            new AppointmentError(nameof(ScheduleAppointmentCommand.StartLocal), code),
            new AppointmentError(nameof(ScheduleAppointmentCommand.EndLocal), code)
        }));

    private static AppointmentResult Error(AppointmentResultStatus status, string field, string code) =>
        new(status, Array.AsReadOnly(new[] { new AppointmentError(field, code) }));
}
