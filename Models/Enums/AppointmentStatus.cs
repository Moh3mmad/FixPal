namespace FixPal.Models.Enums;

// Scheduling occupancy/history only; MaintenanceRequest governs actual service work.
public enum AppointmentStatus
{
    Confirmed = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    Superseded = 5,
    Proposed = 6,
    Rejected = 7
}
