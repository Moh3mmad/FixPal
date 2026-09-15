using System.ComponentModel.DataAnnotations;
namespace FixPal.Models.Enums;
public enum MaintenanceRequestStatus
{
    [Display(Name = "بانتظار القبول")] Pending = 1,
    [Display(Name = "تم القبول")] Accepted = 2,
    [Display(Name = "قيد التنفيذ")] InProgress = 3,
    [Display(Name = "مكتمل")] Completed = 4,
    [Display(Name = "ملغي")] Cancelled = 5
}
