namespace FixPal.Services;

public sealed record ScheduleAppointmentCommand(
    int MaintenanceRequestId,
    DateTime StartLocal,
    DateTime EndLocal);

public enum AppointmentResultStatus
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    ValidationFailed = 3,
    Conflict = 4
}

public sealed record AppointmentError(string Field, string Code);

public sealed record AppointmentResult(
    AppointmentResultStatus Status,
    IReadOnlyList<AppointmentError> Errors);
