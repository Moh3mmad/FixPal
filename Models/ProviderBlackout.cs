namespace FixPal.Models;

public class ProviderBlackout
{
    public int Id { get; set; }
    public int ProviderProfileId { get; set; }
    public ProviderCalendar ProviderCalendar { get; set; } = null!;
    // Preserve the original interval; corrections remove this row and create another.
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public string? RemovedByUserId { get; set; }
}
