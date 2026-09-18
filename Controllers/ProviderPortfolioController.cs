using System.Data;
using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Controllers;

[Authorize(Roles = AppRoles.Provider), ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ProviderPortfolioController(ApplicationDbContext db, RequestAccessService access,
    IPortfolioMediaStorage storage, ILogger<ProviderPortfolioController> logger) : Controller
{
    private async Task<int?> OwnerId(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        var id = await access.ProviderIdAsync(User, ct);
        return await ProviderEligibility.Active(db).Where(p => p.Id == id).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var ownerId = await OwnerId(ct);
        if (ownerId == null) return Forbid();
        return View(await PagedResult<PortfolioItemViewModel>.CreateAsync(db.ProviderPortfolioItems.AsNoTracking()
            .Where(p => p.ProviderProfileId == ownerId).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
            .Select(PortfolioItemViewModel.Projection), page, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        await OwnerId(ct) == null ? Forbid() : View(new CreatePortfolioItemViewModel());

    [HttpPost, EnableRateLimiting("writes"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Create(CreatePortfolioItemViewModel model, CancellationToken ct)
    {
        var ownerId = await OwnerId(ct);
        if (ownerId == null) return Forbid();
        model.Title = model.Title?.Trim() ?? string.Empty;
        model.Description = model.Description?.Trim();
        if (model.Title.Length is < 1 or > 120) ModelState.AddModelError(nameof(model.Title), "أدخل عنوانًا من 1 إلى 120 حرفًا.");
        if (model.Description?.Length > 500) ModelState.AddModelError(nameof(model.Description), "الوصف لا يتجاوز 500 حرف.");
        if (model.Image == null) ModelState.AddModelError(nameof(model.Image), "اختر صورة للعمل.");
        if (!ModelState.IsValid) return View(model);
        StoredMedia? saved = null;
        var committed = false;
        try
        {
            saved = await storage.SaveAsync(model.Image!, ct);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Recheck current approval/ownership after image processing, within the write transaction.
            if (await OwnerId(ct) != ownerId) return Forbid();
            db.ProviderPortfolioItems.Add(new ProviderPortfolioItem
            {
                ProviderProfileId = ownerId.Value, Title = model.Title, Description = model.Description,
                StorageKey = saved.Key, ContentType = saved.ContentType, CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            committed = true;
            TempData["SuccessMessage"] = "نُشر العمل في ملفك العام.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or DbUpdateException)
        {
            logger.LogWarning(ex, "Portfolio creation failed for provider {ProviderId}", ownerId);
            ModelState.AddModelError(string.Empty, ex is InvalidDataException ? ex.Message : "تعذر حفظ العمل. حاول مجددًا.");
            return View(model);
        }
        finally { if (saved != null && !committed) await storage.DeleteAsync(saved.Key, CancellationToken.None); }
    }

    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        var ownerId = await OwnerId(ct);
        if (ownerId == null) return Forbid();
        var changed = await db.ProviderPortfolioItems.Where(p => p.Id == id && p.ProviderProfileId == ownerId
                && ProviderEligibility.Active(db).Any(owner => owner.Id == p.ProviderProfileId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsArchived, true), ct);
        if (changed == 0) return NotFound();
        TempData["SuccessMessage"] = "أُخفي العمل من الملف العام. الصورة والسجل محفوظان.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        var ownerId = await OwnerId(ct);
        var item = await db.ProviderPortfolioItems.AsNoTracking().Where(p => p.Id == id
            && (p.ProviderProfileId == ownerId || (!p.IsArchived
                && ProviderEligibility.Active(db).Any(owner => owner.Id == p.ProviderProfileId))))
            .Select(p => new { p.StorageKey, p.ContentType }).SingleOrDefaultAsync(ct);
        if (item == null) return NotFound();
        try { return File(await storage.OpenAsync(item.StorageKey, ct), item.ContentType); }
        catch (FileNotFoundException) { return NotFound(); }
    }
}
