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
    private sealed record SchedulingContext(RequestAccess Grant, string ActorId, int ProviderId);
    private sealed record ValidatedInterval(UtcInterval Interval, TimeZoneInfo Zone);

    public async Task<AppointmentPanelReadModel?> GetForRequestAsync(
        ClaimsPrincipal user,
        int maintenanceRequestId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (maintenanceRequestId <= 0) return null;

        var grant = await access.GetAsync(user, maintenanceRequestId, ct);
        var actorId = AuthenticatedActorId(user);
        if (grant is not { CanParticipate: true } || actorId == null) return null;

        var request = grant.Request;
        var hasAgreement = await agreement.AgreedRequests.AsNoTracking()
            .AnyAsync(r => r.Id == request.Id, ct);
        string? calendarTimeZoneId = null;
        bool? calendarEnabled = null;
        var appointments = new List<Appointment>();

        if (request.ProviderProfileId is int providerId)
        {
            var calendar = await db.ProviderCalendars.AsNoTracking()
                .Where(c => c.ProviderProfileId == providerId)
                .Select(c => new { c.TimeZoneId, c.IsEnabled })
                .SingleOrDefaultAsync(ct);
            calendarTimeZoneId = calendar?.TimeZoneId;
            calendarEnabled = calendar?.IsEnabled;
            appointments = await db.Appointments.AsNoTracking()
                .Where(a => a.MaintenanceRequestId == request.Id && a.ProviderProfileId == providerId)
                .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
                .ToListAsync(ct);
        }

        var confirmed = appointments
            .Where(a => a.Status is AppointmentStatus.Confirmed or AppointmentStatus.InProgress)
            .ToList();
        if (confirmed.Count > 1) return null;

        var proposals = appointments
            .Where(a => a.Status == AppointmentStatus.Proposed)
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => ToReadModel(a, actorId, request.Status)).ToList().AsReadOnly();
        var history = appointments
            .Where(a => a.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled
                or AppointmentStatus.Superseded or AppointmentStatus.Rejected)
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => ToReadModel(a, actorId, request.Status)).ToList().AsReadOnly();

        return new AppointmentPanelReadModel(
            request.Id,
            request.Status,
            request.RequestType,
            grant.IsOwner,
            grant.IsProvider,
            hasAgreement,
            calendarTimeZoneId,
            calendarEnabled,
            confirmed.Count == 1 ? ToReadModel(confirmed[0], actorId, request.Status) : null,
            proposals,
            history);
    }

    public Task<AppointmentResult> ScheduleAsync(
        ClaimsPrincipal user,
        ScheduleAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.MaintenanceRequestId <= 0)
            return Task.FromResult(Error(AppointmentResultStatus.ValidationFailed,
                nameof(command.MaintenanceRequestId), "InvalidRequestId"));
        return RunMutationAsync(user, command.MaintenanceRequestId,
            () => ProposeInitialLockedAsync(user, command, ct), ct);
    }

    public Task<AppointmentResult> RescheduleAsync(
        ClaimsPrincipal user,
        RescheduleAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var error = ValidateMutationInput(command.MaintenanceRequestId, command.AppointmentId,
            command.ExpectedAppointmentRowVersion, out var expectedVersion);
        if (error != null) return Task.FromResult(error);
        return RunMutationAsync(user, command.MaintenanceRequestId,
            () => ProposeReplacementLockedAsync(user, command, expectedVersion, ct), ct);
    }

    public Task<AppointmentResult> ConfirmAsync(
        ClaimsPrincipal user,
        AppointmentDecisionCommand command,
        CancellationToken ct) => RunDecisionAsync(user, command, ConfirmLockedAsync, ct);

    public Task<AppointmentResult> RejectAsync(
        ClaimsPrincipal user,
        AppointmentDecisionCommand command,
        CancellationToken ct) => RunDecisionAsync(user, command, RejectLockedAsync, ct);

    public Task<AppointmentResult> CancelAsync(
        ClaimsPrincipal user,
        CancelAppointmentCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var error = ValidateMutationInput(command.MaintenanceRequestId, command.AppointmentId,
            command.ExpectedAppointmentRowVersion, out var expectedVersion);
        if (error != null) return Task.FromResult(error);
        return RunMutationAsync(user, command.MaintenanceRequestId,
            () => CancelLockedAsync(user, command, expectedVersion, ct), ct);
    }

    private Task<AppointmentResult> RunDecisionAsync(
        ClaimsPrincipal user,
        AppointmentDecisionCommand command,
        Func<ClaimsPrincipal, AppointmentDecisionCommand, byte[], CancellationToken, Task<AppointmentResult>> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var error = ValidateMutationInput(command.MaintenanceRequestId, command.AppointmentId,
            command.ExpectedAppointmentRowVersion, out var expectedVersion);
        if (error != null) return Task.FromResult(error);
        return RunMutationAsync(user, command.MaintenanceRequestId,
            () => action(user, command, expectedVersion, ct), ct);
    }

    private async Task<AppointmentResult> RunMutationAsync(
        ClaimsPrincipal user,
        int requestId,
        Func<Task<AppointmentResult>> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (AuthenticatedActorId(user) == null)
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        EnsureIndependentMutation();

        AppointmentResult? callbackResult = null;
        var outcome = await mutations.RunAsync(requestId, async () =>
        {
            callbackResult = await action();
            return ToMutationResult(callbackResult);
        }, ct);
        return FinalResult(outcome, callbackResult);
    }

    private async Task<AppointmentResult> ProposeInitialLockedAsync(
        ClaimsPrincipal user,
        ScheduleAppointmentCommand command,
        CancellationToken ct)
    {
        var (context, authorizationError) = await AuthorizeSchedulableAsync(user, command.MaintenanceRequestId, ct);
        if (authorizationError != null) return authorizationError;

        var calendar = await LockCalendarAsync(context!.ProviderId, ct);
        if (calendar == null) return Conflict("CalendarMissing");
        if (!calendar.IsEnabled) return Conflict("CalendarDisabled");
        if (await db.Appointments.AsNoTracking().AnyAsync(a =>
            a.MaintenanceRequestId == command.MaintenanceRequestId
            && a.ProviderProfileId == context.ProviderId
            && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.InProgress), ct))
            return Conflict("RequestAlreadyScheduled", nameof(command.MaintenanceRequestId));

        var (validated, intervalError) = await ValidateIntervalAsync(
            context.ProviderId, calendar, command.StartLocal, command.EndLocal, ct);
        if (intervalError != null) return intervalError;

        var proposal = NewProposal(context, calendar, validated!.Interval, null);
        return await InsertAsync(proposal, ct);
    }

    private async Task<AppointmentResult> ProposeReplacementLockedAsync(
        ClaimsPrincipal user,
        RescheduleAppointmentCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var (context, authorizationError) = await AuthorizeSchedulableAsync(user, command.MaintenanceRequestId, ct);
        if (authorizationError != null) return authorizationError;

        var calendar = await LockCalendarAsync(context!.ProviderId, ct);
        if (calendar == null) return Conflict("CalendarMissing");
        if (!calendar.IsEnabled) return Conflict("CalendarDisabled");

        var current = await FindAppointmentAsync(command.AppointmentId, command.MaintenanceRequestId,
            context.ProviderId, ct);
        if (current == null) return NotFound(nameof(command.AppointmentId), "AppointmentNotFound");
        if (current.Status != AppointmentStatus.Confirmed)
            return Conflict("AppointmentNotReschedulable", nameof(command.AppointmentId));
        if (!current.RowVersion.AsSpan().SequenceEqual(expectedVersion))
            return Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));
        if (await db.Appointments.AsNoTracking().AnyAsync(a =>
            a.ReplacesAppointmentId == current.Id && a.Status == AppointmentStatus.Proposed, ct))
            return Conflict("ReplacementAlreadyProposed", nameof(command.AppointmentId));

        var (validated, intervalError) = await ValidateIntervalAsync(
            context.ProviderId, calendar, command.StartLocal, command.EndLocal, ct);
        if (intervalError != null) return intervalError;

        var proposal = NewProposal(context, calendar, validated!.Interval, current.Id);
        return await InsertAsync(proposal, ct);
    }

    private async Task<AppointmentResult> ConfirmLockedAsync(
        ClaimsPrincipal user,
        AppointmentDecisionCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var (context, authorizationError) = await AuthorizeSchedulableAsync(user, command.MaintenanceRequestId, ct);
        if (authorizationError != null) return authorizationError;

        var calendar = await LockCalendarAsync(context!.ProviderId, ct);
        if (calendar == null) return Conflict("CalendarMissing");
        if (!calendar.IsEnabled) return Conflict("CalendarDisabled");

        var proposal = await FindAppointmentAsync(command.AppointmentId, command.MaintenanceRequestId,
            context.ProviderId, ct);
        var proposalError = ValidateProposalDecision(proposal, context.ActorId, expectedVersion, command);
        if (proposalError != null) return proposalError;

        var zone = ResolveTimeZone(calendar.TimeZoneId);
        if (zone == null) return Conflict("CalendarTimeZoneUnavailable");
        var interval = new UtcInterval(proposal!.StartUtc, proposal.EndUtc);
        if (!timePolicy.IsFutureStart(interval.StartUtc))
            return Error(AppointmentResultStatus.ValidationFailed, nameof(proposal.StartUtc), "StartMustBeFuture");
        var periods = await WorkingPeriodsAsync(context.ProviderId, ct);
        if (!timePolicy.FitsWorkingPeriod(interval, zone, periods))
            return IntervalError(AppointmentResultStatus.ValidationFailed, "OutsideWorkingHours");
        if (await HasBlackoutConflictAsync(context.ProviderId, interval, ct))
            return IntervalError(AppointmentResultStatus.Conflict, "BlackoutConflict");

        Appointment? replaced = null;
        var replacesConfirmedAppointment = false;
        if (proposal.ReplacesAppointmentId is int replacedId)
        {
            replaced = await FindAppointmentAsync(replacedId, command.MaintenanceRequestId, context.ProviderId, ct);
            replacesConfirmedAppointment = replaced?.Status == AppointmentStatus.Confirmed;
            // The former direct-reschedule write used one timestamp and actor to supersede
            // the old row and create its replacement. The migration keeps that exact audit
            // fingerprint while converting the unilateral replacement into a proposal.
            var isMigratedDirectReschedule = replaced is
                {
                    Status: AppointmentStatus.Superseded,
                    ClosedAtUtc: not null,
                    ClosedByUserId: not null
                }
                && replaced.ClosedAtUtc == proposal.CreatedAtUtc
                && replaced.ClosedByUserId == proposal.CreatedByUserId;
            if (!replacesConfirmedAppointment && !isMigratedDirectReschedule)
                return Conflict("ReplacedAppointmentChanged", nameof(command.AppointmentId));
        }

        var excludedId = replacesConfirmedAppointment ? replaced!.Id : 0;
        var providerConflict = await db.Appointments.AsNoTracking().AnyAsync(a =>
            a.ProviderProfileId == context.ProviderId && a.Id != excludedId
            && ((a.Status == AppointmentStatus.Confirmed
                    && a.StartUtc < interval.EndUtc && interval.StartUtc < a.EndUtc)
                || (a.Status == AppointmentStatus.InProgress && a.StartUtc < interval.EndUtc)), ct);
        if (providerConflict) return IntervalError(AppointmentResultStatus.Conflict, "ProviderTimeConflict");

        var requestConflict = await db.Appointments.AsNoTracking().AnyAsync(a =>
            a.MaintenanceRequestId == command.MaintenanceRequestId && a.Id != excludedId
            && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.InProgress), ct);
        if (requestConflict) return Conflict("RequestAlreadyScheduled", nameof(command.MaintenanceRequestId));

        var now = clock.GetUtcNow();
        if (replacesConfirmedAppointment)
        {
            var oldChanged = await db.Appointments.Where(a => a.Id == replaced!.Id
                    && a.MaintenanceRequestId == command.MaintenanceRequestId
                    && a.ProviderProfileId == context.ProviderId
                    && a.Status == AppointmentStatus.Confirmed && a.RowVersion == replaced.RowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, AppointmentStatus.Superseded)
                    .SetProperty(a => a.ClosedAtUtc, now)
                    .SetProperty(a => a.ClosedByUserId, context.ActorId), ct);
            if (oldChanged != 1) return Conflict("ReplacedAppointmentChanged", nameof(command.AppointmentId));
        }

        var changed = await db.Appointments.Where(a => a.Id == proposal.Id
                && a.MaintenanceRequestId == command.MaintenanceRequestId
                && a.ProviderProfileId == context.ProviderId
                && a.Status == AppointmentStatus.Proposed && a.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Confirmed)
                .SetProperty(a => a.DecisionAtUtc, now)
                .SetProperty(a => a.DecisionByUserId, context.ActorId), ct);
        return changed == 1 ? Success() : Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));
    }

    private async Task<AppointmentResult> RejectLockedAsync(
        ClaimsPrincipal user,
        AppointmentDecisionCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var (context, authorizationError) = await AuthorizeSchedulableAsync(user, command.MaintenanceRequestId, ct);
        if (authorizationError != null) return authorizationError;
        var calendar = await LockCalendarAsync(context!.ProviderId, ct);
        if (calendar == null) return Conflict("CalendarMissing");

        var proposal = await FindAppointmentAsync(command.AppointmentId, command.MaintenanceRequestId,
            context.ProviderId, ct);
        var proposalError = ValidateProposalDecision(proposal, context.ActorId, expectedVersion, command);
        if (proposalError != null) return proposalError;

        var now = clock.GetUtcNow();
        var changed = await db.Appointments.Where(a => a.Id == proposal!.Id
                && a.MaintenanceRequestId == command.MaintenanceRequestId
                && a.ProviderProfileId == context.ProviderId
                && a.Status == AppointmentStatus.Proposed && a.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Rejected)
                .SetProperty(a => a.DecisionAtUtc, now)
                .SetProperty(a => a.DecisionByUserId, context.ActorId)
                .SetProperty(a => a.ClosedAtUtc, now)
                .SetProperty(a => a.ClosedByUserId, context.ActorId), ct);
        return changed == 1 ? Success() : Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));
    }

    private async Task<AppointmentResult> CancelLockedAsync(
        ClaimsPrincipal user,
        CancelAppointmentCommand command,
        byte[] expectedVersion,
        CancellationToken ct)
    {
        var actorId = AuthenticatedActorId(user)!;
        var grant = await access.GetAsync(user, command.MaintenanceRequestId, ct);
        var request = grant?.Request ?? await db.MaintenanceRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == command.MaintenanceRequestId, ct);
        if (request == null) return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");

        var assignedProviderUserId = request.ProviderProfileId == null ? null :
            await db.ProviderProfiles.AsNoTracking().Where(p => p.Id == request.ProviderProfileId.Value)
                .Select(p => p.UserId).SingleOrDefaultAsync(ct);
        var isOwner = request.CustomerId == actorId;
        var isProvider = assignedProviderUserId == actorId;
        if (!(isOwner || isProvider) || (isOwner && isProvider))
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed");
        if (request.RequestType != RequestType.PrivateService || request.ProviderProfileId == null
            || request.Status != MaintenanceRequestStatus.Accepted)
            return Conflict("RequestNotSchedulable", nameof(command.MaintenanceRequestId));

        var providerId = request.ProviderProfileId.Value;
        var calendar = await LockCalendarAsync(providerId, ct);
        if (calendar == null) return Conflict("CalendarMissing");
        var appointment = await FindAppointmentAsync(command.AppointmentId, request.Id, providerId, ct);
        if (appointment == null) return NotFound(nameof(command.AppointmentId), "AppointmentNotFound");
        if (!appointment.RowVersion.AsSpan().SequenceEqual(expectedVersion))
            return Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));

        var mayWithdraw = appointment.Status == AppointmentStatus.Proposed
            && appointment.CreatedByUserId == actorId;
        var mayCancelConfirmed = appointment.Status == AppointmentStatus.Confirmed;
        if (!mayWithdraw && !mayCancelConfirmed)
            return appointment.Status == AppointmentStatus.Proposed
                ? Error(AppointmentResultStatus.Forbidden, string.Empty, "OnlyProposerMayWithdraw")
                : Conflict("AppointmentNotCancellable", nameof(command.AppointmentId));

        var now = clock.GetUtcNow();
        var expectedStatus = appointment.Status;
        var changed = await db.Appointments.Where(a => a.Id == appointment.Id
                && a.MaintenanceRequestId == request.Id && a.ProviderProfileId == providerId
                && a.Status == expectedStatus && a.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Cancelled)
                .SetProperty(a => a.ClosedAtUtc, now)
                .SetProperty(a => a.ClosedByUserId, actorId), ct);
        return changed == 1 ? Success() : Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));
    }

    private async Task<(SchedulingContext? Context, AppointmentResult? Error)> AuthorizeSchedulableAsync(
        ClaimsPrincipal user,
        int requestId,
        CancellationToken ct)
    {
        var actorId = AuthenticatedActorId(user);
        var grant = await access.GetAsync(user, requestId, ct);
        if (actorId == null || grant is not { CanParticipate: true } || (grant.IsOwner && grant.IsProvider))
            return (null, Error(AppointmentResultStatus.Forbidden, string.Empty, "ActorNotAllowed"));
        var request = grant.Request;
        if (request.RequestType != RequestType.PrivateService || request.ProviderProfileId == null
            || request.Status != MaintenanceRequestStatus.Accepted)
            return (null, Conflict("RequestNotSchedulable", nameof(requestId)));
        if (!await agreement.AgreedRequests.AnyAsync(r => r.Id == request.Id, ct))
            return (null, Conflict("AgreementRequired", nameof(requestId)));
        var providerId = request.ProviderProfileId.Value;
        if (!await ProviderEligibility.ForRequest(db, request.ServiceCategoryId, request.AreaId, request.CustomerId)
            .AnyAsync(p => p.Id == providerId, ct))
            return (null, Conflict("ProviderNotEligible", nameof(requestId)));
        return (new SchedulingContext(grant, actorId, providerId), null);
    }

    private async Task<(ValidatedInterval? Value, AppointmentResult? Error)> ValidateIntervalAsync(
        int providerId,
        ProviderCalendar calendar,
        DateTime startLocal,
        DateTime endLocal,
        CancellationToken ct)
    {
        var zone = ResolveTimeZone(calendar.TimeZoneId);
        if (zone == null) return (null, Conflict("CalendarTimeZoneUnavailable"));
        var resolved = timePolicy.ResolveLocalInterval(startLocal, endLocal, zone);
        if (resolved.Interval == null) return (null, TimeFailure(resolved.Error));
        if (!timePolicy.IsFutureStart(resolved.Interval.StartUtc))
            return (null, Error(AppointmentResultStatus.ValidationFailed,
                nameof(ScheduleAppointmentCommand.StartLocal), "StartMustBeFuture"));
        var periods = await WorkingPeriodsAsync(providerId, ct);
        if (!timePolicy.FitsWorkingPeriod(resolved.Interval, zone, periods))
            return (null, IntervalError(AppointmentResultStatus.ValidationFailed, "OutsideWorkingHours"));
        if (await HasBlackoutConflictAsync(providerId, resolved.Interval, ct))
            return (null, IntervalError(AppointmentResultStatus.Conflict, "BlackoutConflict"));
        return (new ValidatedInterval(resolved.Interval, zone), null);
    }

    private static AppointmentResult? ValidateProposalDecision(
        Appointment? proposal,
        string actorId,
        byte[] expectedVersion,
        AppointmentDecisionCommand command)
    {
        if (proposal == null) return NotFound(nameof(command.AppointmentId), "AppointmentNotFound");
        if (proposal.Status != AppointmentStatus.Proposed)
            return Conflict("AppointmentNotProposed", nameof(command.AppointmentId));
        if (!proposal.RowVersion.AsSpan().SequenceEqual(expectedVersion))
            return Conflict("AppointmentChanged", nameof(command.ExpectedAppointmentRowVersion));
        if (proposal.CreatedByUserId == actorId)
            return Error(AppointmentResultStatus.Forbidden, string.Empty, "ProposerCannotDecide");
        return null;
    }

    private Appointment NewProposal(
        SchedulingContext context,
        ProviderCalendar calendar,
        UtcInterval interval,
        int? replacesAppointmentId) => new()
    {
        MaintenanceRequestId = context.Grant.Request.Id,
        ProviderProfileId = context.ProviderId,
        StartUtc = interval.StartUtc,
        EndUtc = interval.EndUtc,
        TimeZoneId = calendar.TimeZoneId,
        Status = AppointmentStatus.Proposed,
        CreatedAtUtc = clock.GetUtcNow(),
        CreatedByUserId = context.ActorId,
        ReplacesAppointmentId = replacesAppointmentId
    };

    private async Task<AppointmentResult> InsertAsync(Appointment appointment, CancellationToken ct)
    {
        try
        {
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(ct);
            return Success();
        }
        finally
        {
            db.Entry(appointment).State = EntityState.Detached;
        }
    }

    private Task<Appointment?> FindAppointmentAsync(int id, int requestId, int providerId, CancellationToken ct) =>
        db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id
            && a.MaintenanceRequestId == requestId && a.ProviderProfileId == providerId, ct);

    private static AppointmentResult? ValidateMutationInput(
        int requestId,
        int appointmentId,
        string? encodedRowVersion,
        out byte[] expectedVersion)
    {
        expectedVersion = new byte[8];
        if (requestId <= 0)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(requestId), "InvalidRequestId");
        if (appointmentId <= 0)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(appointmentId), "InvalidAppointmentId");
        if (encodedRowVersion == null
            || !Convert.TryFromBase64String(encodedRowVersion, expectedVersion, out var written)
            || written != expectedVersion.Length)
            return Error(AppointmentResultStatus.ValidationFailed, nameof(encodedRowVersion), "InvalidRowVersion");
        return null;
    }

    private void EnsureIndependentMutation()
    {
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Appointment mutations require RequestMutationService to own the transaction.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Appointment mutations require a context without pending changes.");
    }

    private Task<ProviderCalendar?> LockCalendarAsync(int providerId, CancellationToken ct) =>
        db.ProviderCalendars.FromSqlInterpolated(
            $"SELECT * FROM [ProviderCalendars] WITH (UPDLOCK, HOLDLOCK) WHERE [ProviderProfileId] = {providerId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);

    private Task<List<WorkingPeriodDefinition>> WorkingPeriodsAsync(int providerId, CancellationToken ct) =>
        db.ProviderWorkingPeriods.AsNoTracking().Where(p => p.ProviderProfileId == providerId)
            .OrderBy(p => p.DayOfWeek).ThenBy(p => p.StartLocal).ThenBy(p => p.EndLocal)
            .Select(p => new WorkingPeriodDefinition(p.DayOfWeek, p.StartLocal, p.EndLocal)).ToListAsync(ct);

    private Task<bool> HasBlackoutConflictAsync(int providerId, UtcInterval interval, CancellationToken ct) =>
        db.ProviderBlackouts.AsNoTracking().AnyAsync(b => b.ProviderProfileId == providerId
            && b.RemovedAtUtc == null && b.StartUtc < interval.EndUtc && interval.StartUtc < b.EndUtc, ct);

    private static TimeZoneInfo? ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static AppointmentReadModel ToReadModel(Appointment appointment, string actorId,
        MaintenanceRequestStatus requestStatus)
    {
        var isCreator = appointment.CreatedByUserId == actorId;
        var isProposal = appointment.Status == AppointmentStatus.Proposed;
        return new AppointmentReadModel(
            appointment.Id, appointment.StartUtc, appointment.EndUtc, appointment.TimeZoneId,
            appointment.Status, Convert.ToBase64String(appointment.RowVersion),
            appointment.ReplacesAppointmentId, isCreator, appointment.CreatedAtUtc,
            appointment.DecisionAtUtc,
            isProposal && !isCreator,
            isProposal && !isCreator,
            isProposal && isCreator,
            appointment.Status == AppointmentStatus.Confirmed && requestStatus == MaintenanceRequestStatus.Accepted);
    }

    private static string? AuthenticatedActorId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    private static MutationResult ToMutationResult(AppointmentResult result) => result.Status switch
    {
        AppointmentResultStatus.Success => MutationResult.Success,
        AppointmentResultStatus.NotFound => MutationResult.NotFound,
        _ => MutationResult.Conflict
    };

    private static AppointmentResult FinalResult(MutationResult outcome, AppointmentResult? callback) => outcome switch
    {
        MutationResult.Success when callback is { Status: AppointmentResultStatus.Success } => callback,
        MutationResult.NotFound => callback is { Status: AppointmentResultStatus.NotFound }
            ? callback : NotFound(string.Empty, "RequestNotFound"),
        MutationResult.Conflict when callback is { Status: not AppointmentResultStatus.Success } => callback,
        _ => Conflict("ConcurrentSchedulingConflict")
    };

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
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null)
        };
        return error == SchedulingTimeError.InvalidInterval
            ? Error(AppointmentResultStatus.ValidationFailed, nameof(ScheduleAppointmentCommand.EndLocal), code)
            : IntervalError(AppointmentResultStatus.ValidationFailed, code);
    }

    private static AppointmentResult Success() => new(AppointmentResultStatus.Success, []);
    private static AppointmentResult Conflict(string code, string field = "") =>
        Error(AppointmentResultStatus.Conflict, field, code);
    private static AppointmentResult NotFound(string field, string code) =>
        Error(AppointmentResultStatus.NotFound, field, code);
    private static AppointmentResult IntervalError(AppointmentResultStatus status, string code) =>
        new(status, [new(nameof(ScheduleAppointmentCommand.StartLocal), code),
            new(nameof(ScheduleAppointmentCommand.EndLocal), code)]);
    private static AppointmentResult Error(AppointmentResultStatus status, string field, string code) =>
        new(status, [new(field, code)]);
}
