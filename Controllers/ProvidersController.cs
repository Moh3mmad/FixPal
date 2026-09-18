using FixPal.Data;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[AllowAnonymous]
public class ProvidersController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? categoryId, int? cityId, int page = 1, CancellationToken ct = default)
    {
        var query = db.ProviderProfiles.AsNoTracking().Where(p => p.ApprovalStatus == ApprovalStatus.Approved && p.Area!.City.IsActive);
        if (categoryId.HasValue) query = query.Where(p => p.ServiceCategoryId == categoryId);
        if (cityId.HasValue) query = query.Where(p => p.Area!.CityId == cityId);
        return View(new ProviderDirectoryViewModel
        {
            CategoryId = categoryId, CityId = cityId,
            Results = await PagedResult<PublicProviderViewModel>.CreateAsync(query.OrderBy(p => p.DisplayName).ThenBy(p => p.Id).Select(PublicProviderViewModel.Projection), page, ct),
            Categories = await db.ServiceCategories.AsNoTracking().OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct),
            Cities = await db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct)
        });
    }
    [HttpGet]
    public async Task<IActionResult> Details(int id, int page = 1, CancellationToken ct = default, int portfolioPage = 1)
    {
        var model = await db.ProviderProfiles.AsNoTracking()
            .Where(p => p.Id == id && p.ApprovalStatus == ApprovalStatus.Approved && p.Area!.City.IsActive)
            .Select(PublicProviderViewModel.Projection).SingleOrDefaultAsync(ct);
        if (model == null) return NotFound();
        model.Portfolio = await PagedResult<PortfolioItemViewModel>.CreateAsync(db.ProviderPortfolioItems.AsNoTracking()
            .Where(p => p.ProviderProfileId == id && !p.IsArchived
                && FixPal.Services.ProviderEligibility.Active(db).Any(owner => owner.Id == p.ProviderProfileId))
            .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Select(PortfolioItemViewModel.Projection), portfolioPage, ct, 6);
        model.CompletedServiceCount = await new FixPal.Services.RequestAgreementPolicy(db).AgreedRequests
            .CountAsync(r => r.ProviderProfileId == id && r.Status == MaintenanceRequestStatus.Completed && r.CompletedAtUtc != null
                && db.RequestQuotes.Any(q => q.MaintenanceRequestId == r.Id && q.FinalPrice != null && q.FinalPriceAcceptedAtUtc != null), ct);
        model.Reviews = await PagedResult<PublicReviewItem>.CreateAsync(db.ProviderReviews.AsNoTracking().Where(r => r.ProviderProfileId == id)
            .OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Select(r => new PublicReviewItem(r.Rating, r.Comment, r.CreatedAtUtc)), page, ct, 10);
        return View(model);
    }
}
