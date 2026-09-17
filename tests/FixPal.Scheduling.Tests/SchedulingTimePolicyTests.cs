using FixPal.Services;
using Xunit;

namespace FixPal.Scheduling.Tests;

public sealed class SchedulingTimePolicyTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly SchedulingTimePolicy _policy = new(new FixedTimeProvider(Now));

    [Fact]
    public void ResolveLocalInterval_ValidInterval_ReturnsZeroOffsetUtc()
    {
        var result = _policy.ResolveLocalInterval(Local(2030, 1, 7, 9), Local(2030, 1, 7, 10), TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Interval);
        Assert.Equal(TimeSpan.Zero, result.Interval.StartUtc.Offset);
        Assert.Equal(TimeSpan.Zero, result.Interval.EndUtc.Offset);
        Assert.Equal(new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.Zero), result.Interval.StartUtc);
    }

    [Fact]
    public void ResolveLocalInterval_NonUnspecifiedKind_IsRejected()
    {
        var result = _policy.ResolveLocalInterval(
            new DateTime(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc),
            Local(2030, 1, 7, 10),
            TimeZoneInfo.Utc);

        Assert.Equal(SchedulingTimeError.InvalidDateTimeKind, result.Error);
    }

    [Theory]
    [InlineData(30, 0)]
    [InlineData(0, 1)]
    public void ResolveLocalInterval_SubMinutePrecision_IsRejected(int seconds, int ticks)
    {
        var start = Local(2030, 1, 7, 9).AddSeconds(seconds).AddTicks(ticks);
        var result = _policy.ResolveLocalInterval(start, Local(2030, 1, 7, 10), TimeZoneInfo.Utc);

        Assert.Equal(SchedulingTimeError.MinutePrecisionRequired, result.Error);
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(9, 10)]
    public void ResolveLocalInterval_EndNotAfterStart_IsRejected(int endHour, int startHour)
    {
        var result = _policy.ResolveLocalInterval(
            Local(2030, 1, 7, startHour),
            Local(2030, 1, 7, endHour),
            TimeZoneInfo.Utc);

        Assert.Equal(SchedulingTimeError.InvalidInterval, result.Error);
    }

    [Fact]
    public void ResolveLocalInterval_NonexistentDstTime_IsRejected()
    {
        var zone = CreateDstZone();
        var nonexistent = Local(2030, 3, 10, 2, 30);
        Assert.True(zone.IsInvalidTime(nonexistent));

        var result = _policy.ResolveLocalInterval(nonexistent, Local(2030, 3, 10, 4), zone);

        Assert.Equal(SchedulingTimeError.NonexistentLocalTime, result.Error);
    }

    [Fact]
    public void ResolveLocalInterval_AmbiguousDstTime_IsRejected()
    {
        var zone = CreateDstZone();
        var ambiguous = Local(2030, 11, 3, 1, 30);
        Assert.True(zone.IsAmbiguousTime(ambiguous));

        var result = _policy.ResolveLocalInterval(ambiguous, Local(2030, 11, 3, 3), zone);

        Assert.Equal(SchedulingTimeError.AmbiguousLocalTime, result.Error);
    }

    [Fact]
    public void FitsWorkingPeriod_IntervalInsideSinglePeriod_ReturnsTrue()
    {
        var localStart = Local(2030, 1, 7, 10);
        var interval = _policy.ResolveLocalInterval(localStart, Local(2030, 1, 7, 11), TimeZoneInfo.Utc).Interval!;
        var periods = new[] { new WorkingPeriodDefinition(localStart.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(17, 0)) };

        Assert.True(_policy.FitsWorkingPeriod(interval, TimeZoneInfo.Utc, periods));
    }

    [Fact]
    public void FitsWorkingPeriod_IntervalOutsidePeriod_ReturnsFalse()
    {
        var localStart = Local(2030, 1, 7, 8);
        var interval = _policy.ResolveLocalInterval(localStart, Local(2030, 1, 7, 9), TimeZoneInfo.Utc).Interval!;
        var periods = new[] { new WorkingPeriodDefinition(localStart.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(17, 0)) };

        Assert.False(_policy.FitsWorkingPeriod(interval, TimeZoneInfo.Utc, periods));
    }

    [Fact]
    public void FitsWorkingPeriod_IntervalCrossingLocalDate_ReturnsFalse()
    {
        var localStart = Local(2030, 1, 7, 23, 30);
        var interval = _policy.ResolveLocalInterval(localStart, Local(2030, 1, 8, 0, 30), TimeZoneInfo.Utc).Interval!;
        var periods = new[] { new WorkingPeriodDefinition(localStart.DayOfWeek, TimeOnly.MinValue, new TimeOnly(23, 59)) };

        Assert.False(_policy.FitsWorkingPeriod(interval, TimeZoneInfo.Utc, periods));
    }

    [Fact]
    public void Overlaps_ActualOverlap_ReturnsTrue()
    {
        var first = Interval(9, 10);
        var second = Interval(9, 30, 10, 30);

        Assert.True(SchedulingTimePolicy.Overlaps(first, second));
    }

    [Fact]
    public void Overlaps_TouchingBoundary_ReturnsFalse()
    {
        var first = Interval(9, 10);
        var second = Interval(10, 11);

        Assert.False(SchedulingTimePolicy.Overlaps(first, second));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsFutureStart_UsesInjectedClock(int minuteOffset, bool expected)
    {
        Assert.Equal(expected, _policy.IsFutureStart(Now.AddMinutes(minuteOffset)));
    }

    private static DateTime Local(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static UtcInterval Interval(int startHour, int endHour) =>
        Interval(startHour, 0, endHour, 0);

    private static UtcInterval Interval(int startHour, int startMinute, int endHour, int endMinute) => new(
        new DateTimeOffset(2030, 1, 7, startHour, startMinute, 0, TimeSpan.Zero),
        new DateTimeOffset(2030, 1, 7, endHour, endMinute, 0, TimeSpan.Zero));

    private static TimeZoneInfo CreateDstZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2040, 12, 31),
            TimeSpan.FromHours(1),
            start,
            end);
        return TimeZoneInfo.CreateCustomTimeZone(
            "FixPal-Test-DST",
            TimeSpan.FromHours(-5),
            "FixPal test time",
            "FixPal test standard time",
            "FixPal test daylight time",
            new[] { rule });
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
