using FixPal.Models.Enums;

namespace FixPal.Services;

public sealed record ScheduleAppointmentCommand(int MaintenanceRequestId, DateTime StartLocal, DateTime EndLocal);

public sealed record RescheduleAppointmentCommand(
    int MaintenanceRequestId,
    int AppointmentId,
    DateTime StartLocal,
    DateTime EndLocal,
    string? ExpectedAppointmentRowVersion);

public sealed record AppointmentDecisionCommand(
    int MaintenanceRequestId,
    int AppointmentId,
    string? ExpectedAppointmentRowVersion);

public sealed record CancelAppointmentCommand(
    int MaintenanceRequestId,
    int AppointmentId,
    string? ExpectedAppointmentRowVersion);

public sealed record AppointmentReadModel(
    int Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string TimeZoneId,
    AppointmentStatus Status,
    string RowVersion,
    int? ReplacesAppointmentId,
    bool CreatedByCurrentUser,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecisionAtUtc,
    bool CanConfirm,
    bool CanReject,
    bool CanWithdraw,
    bool CanCancel);

public sealed record AppointmentPanelReadModel(
    int MaintenanceRequestId,
    MaintenanceRequestStatus RequestStatus,
    RequestType RequestType,
    bool IsOwner,
    bool IsAssignedProvider,
    bool HasAcceptedAgreement,
    string? CalendarTimeZoneId,
    bool? CalendarEnabled,
    AppointmentReadModel? ConfirmedAppointment,
    IReadOnlyList<AppointmentReadModel> PendingProposals,
    IReadOnlyList<AppointmentReadModel> History);

public enum AppointmentResultStatus
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    ValidationFailed = 3,
    Conflict = 4
}

public sealed record AppointmentError(string Field, string Code);

public sealed record AppointmentResult(AppointmentResultStatus Status, IReadOnlyList<AppointmentError> Errors);
