namespace FixPal.Services;

public sealed record CalendarWorkingPeriod(DayOfWeek DayOfWeek, TimeOnly StartLocal, TimeOnly EndLocal);

// RowVersion is the base64 encoding of SQL Server's eight-byte rowversion.
public sealed record ProviderCalendarReadModel(
    string TimeZoneId,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<CalendarWorkingPeriod> WorkingPeriods);

public sealed record CreateProviderCalendarCommand(
    string? TimeZoneId,
    bool IsEnabled,
    IReadOnlyList<CalendarWorkingPeriod>? WorkingPeriods);

public sealed record UpdateProviderCalendarCommand(
    string? TimeZoneId,
    bool IsEnabled,
    IReadOnlyList<CalendarWorkingPeriod>? WorkingPeriods,
    string? ExpectedRowVersion);

public enum ProviderCalendarStatus
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    ValidationFailed = 3,
    Stale = 4,
    Conflict = 5
}

// Codes are service-level identifiers; a later MVC surface can localize them.
public sealed record ProviderCalendarError(string Field, string Code);

public sealed record ProviderCalendarResult(
    ProviderCalendarStatus Status,
    IReadOnlyList<ProviderCalendarError> Errors);
