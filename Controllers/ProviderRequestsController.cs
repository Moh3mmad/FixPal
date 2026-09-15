using FixPal.Data;
using FixPal.Infrastructure;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize(Roles = AppRoles.Provider)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ProviderRequestsController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    private async Task<int?> ApprovedProviderId(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user == null || !await users.IsInRoleAsync(user, AppRoles.Provider)) return null;
        return await db.ProviderProfiles.AsNoTracking().Where(p => p.UserId == user.Id && p.ApprovalStatus == ApprovalStatus.Approved).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
    }
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, MaintenanceRequestStatus? status = null, CancellationToken ct = default)
    {
        var providerId = await ApprovedProviderId(ct);
        if (!providerId.HasValue) return Forbid();
        var query = db.MaintenanceRequests.AsNoTracking().Where(r => r.ProviderProfileId == providerId);
        if (status.HasValue && Enum.IsDefined(status.Value)) query = query.Where(r => r.Status == status);
        ViewData["StatusFilter"] = status;
        return View(await PagedResult<RequestSummaryViewModel>.CreateAsync(query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Select(RequestSummaryViewModel.Projection), page, ct));
    }
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var providerId = await ApprovedProviderId(ct);
        if (!providerId.HasValue) return Forbid();
        var model = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Id == id && r.ProviderProfileId == providerId)
            .Select(RequestDetailsViewModel.DetailProjection).SingleOrDefaultAsync(ct);
        if (model == null) return NotFound();
        model.CanManage = true;
        model.Quote = await db.RequestQuotes.AsNoTracking().SingleOrDefaultAsync(q => q.MaintenanceRequestId == id, ct);
        model.Review = await db.ProviderReviews.AsNoTracking().SingleOrDefaultAsync(r => r.MaintenanceRequestId == id, ct);
        return View("~/Views/MaintenanceRequests/Details.cshtml", model);
    }
    [HttpPost] public Task<IActionResult> Accept(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.Pending, MaintenanceRequestStatus.Accepted, ct);
    [HttpPost] public Task<IActionResult> Start(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, ct);
    [HttpPost] public Task<IActionResult> Complete(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Completed, ct);
    private async Task<IActionResult> Transition(int id, MaintenanceRequestStatus from, MaintenanceRequestStatus to, CancellationToken ct)
    {
        var providerId = await ApprovedProviderId(ct);
        if (!providerId.HasValue) return Forbid();
        if (!RequestWorkflow.CanTransition(from, to)) return BadRequest();
        var userId = users.GetUserId(User);
        // Status, assignment, approval AND current database role are checked in the UPDATE.
        // Competing submissions cannot both succeed or overwrite timestamps.
        var query = db.MaintenanceRequests.Where(r => r.Id == id && r.ProviderProfileId == providerId && r.Status == from
            && r.ProviderProfile!.ApprovalStatus == ApprovalStatus.Approved
            && db.UserRoles.Any(ur => ur.UserId == userId && db.Roles.Any(role => role.Id == ur.RoleId && role.Name == AppRoles.Provider)));
        var now = DateTime.UtcNow;
        // Once a quote exists, finishing requires the owner's agreement to the final price.
        if (to == MaintenanceRequestStatus.Completed)
            query = query.Where(r => !db.RequestQuotes.Any(q => q.MaintenanceRequestId == r.Id)
                || db.RequestQuotes.Any(q => q.MaintenanceRequestId == r.Id && q.AcceptedAtUtc != null && q.FinalPrice != null && q.FinalPriceAcceptedAtUtc != null));
        var changed = to switch
        {
            MaintenanceRequestStatus.Accepted => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.AcceptedAtUtc, now), ct),
            MaintenanceRequestStatus.InProgress => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.StartedAtUtc, now), ct),
            MaintenanceRequestStatus.Completed => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.CompletedAtUtc, now), ct),
            _ => 0
        };
        if (changed == 0)
        {
            if (!await db.MaintenanceRequests.AnyAsync(r => r.Id == id && r.ProviderProfileId == providerId, ct)) return NotFound();
            TempData["ErrorMessage"] = "لم يتغير الطلب: ربما حُدّثت حالته بالفعل أو لم تعد هذه الخطوة متاحة.";
        }
        else TempData["SuccessMessage"] = "تم تحديث حالة الطلب بنجاح.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
