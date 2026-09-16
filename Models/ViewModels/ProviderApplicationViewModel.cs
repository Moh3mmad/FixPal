using System.ComponentModel.DataAnnotations;

namespace FixPal.Models.ViewModels
{
    public class ProviderApplicationViewModel
    {
        [Required(ErrorMessage = "الاسم المهني مطلوب.")]
        [StringLength(150, MinimumLength = 2,
            ErrorMessage = "الاسم يجب أن يكون بين حرفين و150 حرفًا.")]
        [Display(Name = "الاسم المهني")]
        public string DisplayName { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "اختر تخصصًا صالحًا.")]
        [Display(Name = "التخصص")]
        public int ServiceCategoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "اختر منطقة صالحة.")]
        [Display(Name = "المنطقة")]
        public int AreaId { get; set; }

        [StringLength(500, ErrorMessage = "النبذة لا يمكن أن تتجاوز 500 حرف.")]
        [Display(Name = "نبذة عنك")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "أدخل رقم هاتف للتواصل.")]
        [StringLength(40, ErrorMessage = "رقم الهاتف طويل جدًا.")]
        [Display(Name = "رقم الهاتف")]
        public string? PhoneNumber { get; set; }
    }
}
