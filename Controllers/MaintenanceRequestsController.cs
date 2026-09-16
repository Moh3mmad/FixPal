using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class MaintenanceRequestsController(ApplicationDbContext db, UserManager<ApplicationUser> users, FixPal.Services.ProviderMatchingService matching, FixPal.Services.RequestDetailsService details) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var userId = users.GetUserId(User);
        return View(await PagedResult<RequestSummaryViewModel>.CreateAsync(db.MaintenanceRequests.AsNoTracking()
            .Where(r => r.CustomerId == userId).OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Select(RequestSummaryViewModel.Projection), page, ct));
    }
    [HttpGet]
    public async Task<IActionResult> Create(int? providerProfileId, int? categoryId, RequestType requestType = RequestType.PrivateService, CancellationToken ct = default)
    {
        var model = new CreateMaintenanceRequestViewModel { ServiceCategoryId = categoryId ?? 0, RequestType = Enum.IsDefined(requestType) ? requestType : RequestType.PrivateService };
        if (providerProfileId.HasValue)
        {
            var provider = await FixPal.Services.ProviderEligibility.Active(db).AsNoTracking()
                .Where(p => p.Id == providerProfileId && p.UserId != users.GetUserId(User) && p.ApprovalStatus == ApprovalStatus.Approved && p.Area!.City.IsActive && p.ProviderType == ProviderType.Individual)
                .Select(p => new { p.Id, p.DisplayName, p.ServiceCategoryId, p.AreaId, p.Area!.CityId }).SingleOrDefaultAsync(ct);
            if (provider == null) return NotFound();
            model.ProviderProfileId = provider.Id; model.ServiceCategoryId = provider.ServiceCategoryId;
            model.CityId = provider.CityId; model.AreaId = provider.AreaId; model.SelectedProviderName = provider.DisplayName;
        }
        await LoadOptions(model, ct);
        return View(model);
    }
    [HttpPost, FixPal.Infrastructure.RequireContactPhone, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("writes")]
    public async Task<IActionResult> Create(CreateMaintenanceRequestViewModel model, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        model.Title = model.Title?.Trim() ?? string.Empty;
        model.Description = model.Description?.Trim() ?? string.Empty;
        if (model.Latitude.HasValue != model.Longitude.HasValue || (model.Latitude.HasValue && (!double.IsFinite(model.Latitude.Value) || !double.IsFinite(model.Longitude!.Value))))
            ModelState.AddModelError(nameof(model.Latitude), "حدد إحداثيين صالحين، أو امسح الموقع واستخدم المدينة والمنطقة.");
        if (model.Title.Length < 5) ModelState.AddModelError(nameof(model.Title), "اكتب عنوانًا واضحًا من 5 أحرف على الأقل.");
        if (model.Description.Length < 10) ModelState.AddModelError(nameof(model.Description), "أضف تفاصيل من 10 أحرف على الأقل.");
        if (!await db.ServiceCategories.AnyAsync(c => c.Id == model.ServiceCategoryId, ct))
            ModelState.AddModelError(nameof(model.ServiceCategoryId), "التخصص غير متاح.");
        if (!await db.Areas.AnyAsync(a => a.Id == model.AreaId && a.CityId == model.CityId && a.City.IsActive, ct))
            ModelState.AddModelError(nameof(model.AreaId), "اختر منطقة تابعة للمدينة المحددة.");
        if (model.RequestType == RequestType.PublicReport && model.ProviderProfileId.HasValue)
            ModelState.AddModelError(nameof(model.ProviderProfileId), "البلاغ العام يُسجل بدون تعيين مزود في هذه النسخة.");
        if (model.ProviderProfileId.HasValue && !await FixPal.Services.ProviderEligibility.ForRequest(db, model.ServiceCategoryId, model.AreaId, user.Id).AnyAsync(p => p.Id == model.ProviderProfileId, ct))
            ModelState.AddModelError(nameof(model.ProviderProfileId), "المزود غير متاح لهذا التخصص والمنطقة. اختر مزودًا آخر أو اترك الطلب بدون تعيين.");
        if (!ModelState.IsValid) { await LoadOptions(model, ct); return View(model); }
        var request = new MaintenanceRequest
        {
            CustomerId = user.Id, Title = model.Title, Description = model.Description, RequestType = model.RequestType,
            ServiceCategoryId = model.ServiceCategoryId, AreaId = model.AreaId, ProviderProfileId = model.ProviderProfileId,
            Status = MaintenanceRequestStatus.Pending, CreatedAtUtc = DateTime.UtcNow
            , Latitude = model.Latitude.HasValue ? Math.Round(model.Latitude.Value, 5) : null,
            Longitude = model.Longitude.HasValue ? Math.Round(model.Longitude.Value, 5) : null
        };
        db.MaintenanceRequests.Add(request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        TempData["SuccessMessage"] = "تم تسجيل طلبك. يمكنك متابعة حالته من هذه الصفحة.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }
    [HttpGet]
    public async Task<IActionResult> Details(int id, int quotePage = 1, CancellationToken ct = default)
    {
        var model = await details.GetAsync(User, id, quotePage, ct);
        return model == null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> ProviderOptions(int categoryId, int areaId, string? q, CancellationToken ct)
    {
        q = q?.Trim();
        if (q?.Length > 100) return BadRequest();
        return Json(await matching.FindAsync(categoryId, areaId, q, ct, users.GetUserId(User)));
    }
    private async Task LoadOptions(CreateMaintenanceRequestViewModel model, CancellationToken ct)
    {
        model.Categories = await db.ServiceCategories.AsNoTracking().OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct);
        model.Cities = await db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct);
        model.Areas = await db.Areas.AsNoTracking().Where(a => a.City.IsActive).OrderBy(a => a.City.Name).ThenBy(a => a.Name)
            .Select(a => new AreaOption(a.Id, a.CityId, a.Name + " — " + a.City.Name)).ToListAsync(ct);
        if (model.ProviderProfileId.HasValue)
            model.SelectedProviderName = await db.ProviderProfiles.AsNoTracking().Where(p => p.Id == model.ProviderProfileId && p.ApprovalStatus == ApprovalStatus.Approved).Select(p => p.DisplayName).SingleOrDefaultAsync(ct);
    }
}


