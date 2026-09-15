using FixPal.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FixPal.Models
{
    public class ProviderProfile
    {
        public int Id { get; set; }
        public ProviderAvailability Availability { get; set; } = ProviderAvailability.Unavailable;
        public DateTime? AvailableAfterUtc { get; set; }
        public ICollection<ProviderReview> Reviews { get; set; } = new List<ProviderReview>();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(150)]
        [Display(Name = "اسم مزود الخدمة")]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "نوع مزود الخدمة")]
        public ProviderType ProviderType { get; set; }

        [Required]
        [Display(Name = "حالة الاعتماد")]
        public ApprovalStatus ApprovalStatus { get; set; }
            = ApprovalStatus.Pending;

        [Required]
        [Display(Name = "التخصص")]
        public int ServiceCategoryId { get; set; }

        public ServiceCategory? ServiceCategory { get; set; }

        [Required]
        [Display(Name = "المنطقة")]
        public int AreaId { get; set; }

        public Area? Area { get; set; }

        [StringLength(500)]
        [Display(Name = "نبذة")]
        public string? Description { get; set; }

        [StringLength(250)]
        [Display(Name = "صورة الملف")]
        public string? ProfileImageUrl { get; set; }

        [Display(Name = "تاريخ تقديم الطلب")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
