using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize(Roles = AppRoles.Provider)]
public class ProviderAvailabilityController(ApplicationDbContext db, RequestAccessService access) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var id = await access.ProviderIdAsync(User, ct);
        if (id == null) return Forbid();
        return View(await db.ProviderProfiles.AsNoTracking().Where(p => p.Id == id).Select(p => new AvailabilityViewModel { Availability = p.Availability, AvailableAfterUtc = p.AvailableAfterUtc }).SingleAsync(ct));
    }
    [HttpPost]
    public async Task<IActionResult> Index(AvailabilityViewModel model, CancellationToken ct)
    {
        var id = await access.ProviderIdAsync(User, ct);
        if (id == null) return Forbid();
        if (model.Availability == ProviderAvailability.AvailableAfter && (!model.AvailableAfterUtc.HasValue || model.AvailableAfterUtc <= DateTime.UtcNow))
            ModelState.AddModelError(nameof(model.AvailableAfterUtc), "اختر موعدًا في المستقبل بتوقيت UTC.");
        if (!ModelState.IsValid) return View(model);
        var after = model.Availability == ProviderAvailability.AvailableAfter ? model.AvailableAfterUtc : null;
        await db.ProviderProfiles.Where(p => p.Id == id && p.ApprovalStatus == ApprovalStatus.Approved)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Availability, model.Availability).SetProperty(p => p.AvailableAfterUtc, after), ct);
        TempData["SuccessMessage"] = "تم تحديث توفرّك. الموعد المنقضي يظهر كمتاح الآن تلقائيًا.";
        return RedirectToAction(nameof(Index));
    }
}
