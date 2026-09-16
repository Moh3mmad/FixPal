using FixPal.Models.Enums;

namespace FixPal.Models;

public class Appointment
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public int ProviderProfileId { get; set; }
    public ProviderCalendar ProviderCalendar { get; set; } = null!;
    // Planned instants stay unchanged on rescheduling; a replacement row is created.
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public string? ClosedByUserId { get; set; }
    public int? ReplacesAppointmentId { get; set; }
    public Appointment? ReplacesAppointment { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
