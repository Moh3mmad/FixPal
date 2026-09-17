namespace FixPal.Services;

public sealed record ProviderBlackoutReadModel(
    int Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? RemovedAtUtc,
    string? RemovedByUserId)
{
    public bool IsRemoved => RemovedAtUtc.HasValue;
}

public sealed record ProviderBlackoutManagementReadModel(
    string CalendarRowVersion,
    IReadOnlyList<ProviderBlackoutReadModel> Blackouts);

public sealed record CreateProviderBlackoutCommand(
    DateTime StartLocal,
    DateTime EndLocal,
    string? ExpectedCalendarRowVersion);

public sealed record RemoveProviderBlackoutCommand(
    int BlackoutId,
    string? ExpectedCalendarRowVersion);

public enum ProviderBlackoutStatus
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    ValidationFailed = 3,
    Stale = 4,
    Conflict = 5
}

public sealed record ProviderBlackoutError(string Field, string Code);

public sealed record ProviderBlackoutResult(
    ProviderBlackoutStatus Status,
    IReadOnlyList<ProviderBlackoutError> Errors);
