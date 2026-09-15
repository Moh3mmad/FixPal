using System.ComponentModel.DataAnnotations;
using FixPal.Models.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace FixPal.Models.ViewModels;
public class CreateMaintenanceRequestViewModel
{
    [Range(-90d, 90d, ErrorMessage = "خط العرض غير صالح.")] public double? Latitude { get; set; }
    [Range(-180d, 180d, ErrorMessage = "خط الطول غير صالح.")] public double? Longitude { get; set; }
    [Required(ErrorMessage = "اكتب عنوانًا للمشكلة."), StringLength(150, MinimumLength = 5, ErrorMessage = "العنوان بين 5 و150 حرفًا.")]
    [Display(Name = "عنوان المشكلة")] public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "صف المشكلة لمزود الخدمة."), StringLength(2000, MinimumLength = 10, ErrorMessage = "الوصف بين 10 و2000 حرف.")]
    [Display(Name = "تفاصيل المشكلة")] public string Description { get; set; } = string.Empty;
    [EnumDataType(typeof(RequestType), ErrorMessage = "اختر نوع طلب صالحًا.")]
    [Display(Name = "نوع الطلب")] public RequestType RequestType { get; set; } = RequestType.PrivateService;
    [Range(1, int.MaxValue, ErrorMessage = "اختر التخصص.")]
    [Display(Name = "التخصص")] public int ServiceCategoryId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "اختر المدينة.")]
    [Display(Name = "المدينة")] public int CityId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "اختر المنطقة.")]
    [Display(Name = "المنطقة")] public int AreaId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "اختر مزودًا صالحًا.")]
    [Display(Name = "مزود الخدمة المفضل (اختياري)")] public int? ProviderProfileId { get; set; }
    [ValidateNever] public IReadOnlyList<SelectListItem> Categories { get; set; } = Array.Empty<SelectListItem>();
    [ValidateNever] public IReadOnlyList<SelectListItem> Cities { get; set; } = Array.Empty<SelectListItem>();
    [ValidateNever] public IReadOnlyList<AreaOption> Areas { get; set; } = Array.Empty<AreaOption>();
    [ValidateNever] public string? SelectedProviderName { get; set; }
}
public record AreaOption(int Id, int CityId, string Name);
