namespace FixPal.Services;

public sealed class SchedulingTimePolicy
{
    private readonly TimeProvider _timeProvider;

    public SchedulingTimePolicy(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    // The caller supplies the authoritative calendar timezone; no ID lookup occurs here.
    public LocalIntervalResult ResolveLocalInterval(DateTime startLocal, DateTime endLocal, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (startLocal.Kind != DateTimeKind.Unspecified || endLocal.Kind != DateTimeKind.Unspecified)
            return LocalIntervalResult.Failure(SchedulingTimeError.InvalidDateTimeKind);
        if (!IsMinuteAligned(startLocal.Ticks) || !IsMinuteAligned(endLocal.Ticks))
            return LocalIntervalResult.Failure(SchedulingTimeError.MinutePrecisionRequired);
        if (startLocal >= endLocal)
            return LocalIntervalResult.Failure(SchedulingTimeError.InvalidInterval);
        if (timeZone.IsInvalidTime(startLocal) || timeZone.IsInvalidTime(endLocal))
            return LocalIntervalResult.Failure(SchedulingTimeError.NonexistentLocalTime);
        if (timeZone.IsAmbiguousTime(startLocal) || timeZone.IsAmbiguousTime(endLocal))
            return LocalIntervalResult.Failure(SchedulingTimeError.AmbiguousLocalTime);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone);
        // TimeZoneInfo can clamp at DateTime limits; never accept a changed wall time.
        if (TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone) != startLocal
            || TimeZoneInfo.ConvertTimeFromUtc(endUtc, timeZone) != endLocal)
            return LocalIntervalResult.Failure(SchedulingTimeError.UtcConversionOutOfRange);
        if (startUtc >= endUtc)
            return LocalIntervalResult.Failure(SchedulingTimeError.InvalidInterval);

        return LocalIntervalResult.Success(new UtcInterval(
            new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(endUtc, TimeSpan.Zero)));
    }

    public bool FitsWorkingPeriod(UtcInterval interval, TimeZoneInfo timeZone,
        IReadOnlyCollection<WorkingPeriodDefinition> periods)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(periods);
        var startLocal = TimeZoneInfo.ConvertTime(interval.StartUtc, timeZone).DateTime;
        var endLocal = TimeZoneInfo.ConvertTime(interval.EndUtc, timeZone).DateTime;
        if (startLocal.Date != endLocal.Date) return false;

        // Also reject ambiguous/non-minute appointment input reconstructed from UTC.
        var resolved = ResolveLocalInterval(startLocal, endLocal, timeZone);
        if (resolved.Interval != interval) return false;

        var date = DateOnly.FromDateTime(startLocal);
        foreach (var period in periods)
        {
            if (period is null || !Enum.IsDefined(period.DayOfWeek) || period.DayOfWeek != startLocal.DayOfWeek
                || period.StartLocal >= period.EndLocal
                || !IsMinuteAligned(period.StartLocal.Ticks) || !IsMinuteAligned(period.EndLocal.Ticks)) continue;

            var working = ResolveLocalInterval(date.ToDateTime(period.StartLocal), date.ToDateTime(period.EndLocal), timeZone);
            if (working.Interval is { } window && interval.StartUtc >= window.StartUtc && interval.EndUtc <= window.EndUtc)
                return true;
        }
        return false;
    }

    public bool IsFutureStart(DateTimeOffset startUtc)
    {
        if (startUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The start must have a zero UTC offset.", nameof(startUtc));
        return startUtc > _timeProvider.GetUtcNow();
    }

    public static bool Overlaps(UtcInterval first, UtcInterval second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.StartUtc < second.EndUtc && second.StartUtc < first.EndUtc;
    }

    private static bool IsMinuteAligned(long ticks) => ticks % TimeSpan.TicksPerMinute == 0;
}
