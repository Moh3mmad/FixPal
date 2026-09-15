using System.ComponentModel.DataAnnotations;

namespace FixPal.Models
{
    public class ServiceCategory
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "اسم الخدمة")]
        public string Name { get; set; } = string.Empty;

        [StringLength(250)]
        [Display(Name = "الوصف")]
        public string? Description { get; set; }

        [StringLength(100)]
        public string? Icon { get; set; }
    }
}