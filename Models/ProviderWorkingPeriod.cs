namespace FixPal.Models;

public class ProviderWorkingPeriod
{
    public int Id { get; set; }
    public int ProviderProfileId { get; set; }
    public ProviderCalendar ProviderCalendar { get; set; } = null!;
    // Local civil time in the parent calendar's timezone, not a UTC interval.
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartLocal { get; set; }
    public TimeOnly EndLocal { get; set; }
}
