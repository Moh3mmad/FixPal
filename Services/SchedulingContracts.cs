namespace FixPal.Services;

// Validated half-open interval [StartUtc, EndUtc). No default or mutable invalid state.
public sealed record UtcInterval
{
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }

    public UtcInterval(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (startUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The start must have a zero UTC offset.", nameof(startUtc));
        if (endUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The end must have a zero UTC offset.", nameof(endUtc));
        if (startUtc >= endUtc)
            throw new ArgumentException("The end must be later than the start.", nameof(endUtc));
        StartUtc = startUtc;
        EndUtc = endUtc;
    }
}

public sealed record WorkingPeriodDefinition(DayOfWeek DayOfWeek, TimeOnly StartLocal, TimeOnly EndLocal);

public enum SchedulingTimeError
{
    None = 0,
    InvalidDateTimeKind = 1,
    MinutePrecisionRequired = 2,
    InvalidInterval = 3,
    NonexistentLocalTime = 4,
    AmbiguousLocalTime = 5,
    UtcConversionOutOfRange = 6
}

public sealed record LocalIntervalResult
{
    public UtcInterval? Interval { get; }
    public SchedulingTimeError Error { get; }
    public bool IsSuccess => Interval is not null;

    private LocalIntervalResult(UtcInterval? interval, SchedulingTimeError error)
    {
        Interval = interval;
        Error = error;
    }

    public static LocalIntervalResult Success(UtcInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return new(interval, SchedulingTimeError.None);
    }

    public static LocalIntervalResult Failure(SchedulingTimeError error)
    {
        if (error == SchedulingTimeError.None || !Enum.IsDefined(error))
            throw new ArgumentOutOfRangeException(nameof(error));
        return new(null, error);
    }
}
