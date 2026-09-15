using FixPal.Models.Enums;
namespace FixPal.Infrastructure;
// Shared by the UI and conditional database updates. No client controls status.
public static class RequestWorkflow
{
    public static MaintenanceRequestStatus? Next(MaintenanceRequestStatus status) => status switch
    {
        MaintenanceRequestStatus.Pending => MaintenanceRequestStatus.Accepted,
        MaintenanceRequestStatus.Accepted => MaintenanceRequestStatus.InProgress,
        MaintenanceRequestStatus.InProgress => MaintenanceRequestStatus.Completed,
        _ => null
    };
    public static bool CanTransition(MaintenanceRequestStatus from, MaintenanceRequestStatus to) => Next(from) == to;
    public static string Label(MaintenanceRequestStatus status) => status switch
    {
        MaintenanceRequestStatus.Pending => "بانتظار القبول",
        MaintenanceRequestStatus.Accepted => "تم القبول",
        MaintenanceRequestStatus.InProgress => "قيد التنفيذ",
        MaintenanceRequestStatus.Completed => "مكتمل",
        MaintenanceRequestStatus.Cancelled => "ملغي",
        _ => "غير معروف"
    };
}
