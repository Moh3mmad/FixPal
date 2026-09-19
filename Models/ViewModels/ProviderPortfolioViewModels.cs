using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace FixPal.Models.ViewModels;

public class CreatePortfolioItemViewModel
{
    [Required(ErrorMessage = "أدخل عنوان العمل."), StringLength(120)]
    public string Title { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    [Required(ErrorMessage = "اختر صورة للعمل.")] public IFormFile? Image { get; set; }
}

public record PortfolioItemViewModel(int Id, string Title, string? Description, DateTime CreatedAtUtc, bool IsArchived)
{
    public static Expression<Func<ProviderPortfolioItem, PortfolioItemViewModel>> Projection => p =>
        new(p.Id, p.Title, p.Description, p.CreatedAtUtc, p.IsArchived);
}
