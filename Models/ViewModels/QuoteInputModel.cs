using System.ComponentModel.DataAnnotations;
namespace FixPal.Models.ViewModels;
public class QuoteInputModel
{
    [Range(1, int.MaxValue)] public int? ExpectedRevisionNumber { get; set; }
    [Range(typeof(decimal), "0.01", "1000000")]
    [Display(Name = "الحد الأدنى (شيكل)")] public decimal MinimumPrice { get; set; }
    [Range(typeof(decimal), "0.01", "1000000")]
    [Display(Name = "الحد الأعلى (شيكل)")] public decimal MaximumPrice { get; set; }
    [StringLength(500)] [Display(Name = "ملاحظة قصيرة")] public string? Note { get; set; }
}
