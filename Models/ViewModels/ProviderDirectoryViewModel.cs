using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace FixPal.Models.ViewModels;
public class PublicProviderViewModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AcceptsPrivateRequests { get; set; }
    public Enums.ProviderAvailability Availability { get; set; }
    public DateTime? AvailableAfterUtc { get; set; }
    public int ReviewCount { get; set; }
    public double? AverageRating { get; set; }
    public PagedResult<PublicReviewItem> Reviews { get; set; } = new();
    public static Expression<Func<ProviderProfile, PublicProviderViewModel>> Projection => p => new()
    {
        Id = p.Id, DisplayName = p.DisplayName, Category = p.ServiceCategory!.Name,
        Location = p.Area!.Name + " — " + p.Area.City.Name, Description = p.Description,
        AcceptsPrivateRequests = p.ProviderType == Enums.ProviderType.Individual,
        Availability = p.Availability, AvailableAfterUtc = p.AvailableAfterUtc,
        ReviewCount = p.Reviews.Count(), AverageRating = p.Reviews.Select(r => (double?)r.Rating).Average()
    };
}
public class ProviderDirectoryViewModel
{
    public PagedResult<PublicProviderViewModel> Results { get; set; } = new();
    public int? CategoryId { get; set; }
    public int? CityId { get; set; }
    public IReadOnlyList<SelectListItem> Categories { get; set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> Cities { get; set; } = Array.Empty<SelectListItem>();
}
public record PublicReviewItem(int Rating, string? Comment, DateTime CreatedAtUtc);
