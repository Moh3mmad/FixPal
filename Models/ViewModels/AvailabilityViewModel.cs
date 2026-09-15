using System.ComponentModel.DataAnnotations;
using FixPal.Models.Enums;
namespace FixPal.Models.ViewModels;
public class AvailabilityViewModel
{
    [EnumDataType(typeof(ProviderAvailability))]
    [Display(Name = "حالة التوفر")] public ProviderAvailability Availability { get; set; }
    [Display(Name = "الموعد بتوقيت UTC")] public DateTime? AvailableAfterUtc { get; set; }
}
