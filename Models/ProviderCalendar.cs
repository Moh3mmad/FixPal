namespace FixPal.Models;

public class ProviderCalendar
{
    public int ProviderProfileId { get; set; }
    public ProviderProfile ProviderProfile { get; set; } = null!;
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    // Settings, working-period and blackout writes update this under the calendar lock.
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
