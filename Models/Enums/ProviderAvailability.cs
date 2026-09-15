using System.ComponentModel.DataAnnotations;
namespace FixPal.Models.Enums;
public enum ProviderAvailability
{
    [Display(Name = "غير متاح حاليًا")] Unavailable = 0,
    [Display(Name = "متاح الآن")] AvailableNow = 1,
    [Display(Name = "متاح بعد موعد محدد")] AvailableAfter = 2
}
