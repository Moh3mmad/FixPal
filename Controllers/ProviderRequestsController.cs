using System.Security.Claims;
using FixPal.Data;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using FixPal.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize(Roles = AppRoles.Provider)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ProviderRequestsController(ApplicationDbContext db, RequestAccessService access, RequestWorkflowService workflow, RequestDetailsService details) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, MaintenanceRequestStatus? status = null, CancellationToken ct = default)
    {
        var providerId = await access.ProviderIdAsync(User, ct);
        if (providerId == null) return Forbid();
        var query = db.MaintenanceRequests.AsNoTracking().Where(r => r.ProviderProfileId == providerId);
        if (status.HasValue && Enum.IsDefined(status.Value)) query = query.Where(r => r.Status == status);
        ViewData["StatusFilter"] = status;
        return View(await PagedResult<RequestSummaryViewModel>.CreateAsync(query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Select(RequestSummaryViewModel.Projection), page, ct));
    }
    [HttpGet]
    public async Task<IActionResult> Available(int page = 1, CancellationToken ct = default)
    {
        var providerId = await access.ProviderIdAsync(User, ct);
        if (providerId == null) return Forbid();
        return View(await PagedResult<ClaimableRequestItem>.CreateAsync(workflow.Claimable(providerId.Value, User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            .AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Select(r => new ClaimableRequestItem(r.Id, r.ServiceCategory.Name, r.Area.City.Name + " — " + r.Area.Name, r.CreatedAtUtc)), page, ct));
    }
    [HttpGet]
    public async Task<IActionResult> Details(int id, int quotePage = 1, CancellationToken ct = default)
    {
        var providerId = await access.ProviderIdAsync(User, ct);
        if (providerId == null) return Forbid();
        if (!await db.MaintenanceRequests.AnyAsync(r => r.Id == id && r.ProviderProfileId == providerId, ct)) return NotFound();
        var model = await details.GetAsync(User, id, quotePage, ct);
        return model == null ? NotFound() : View("~/Views/MaintenanceRequests/Details.cshtml", model);
    }
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Claim(int id, CancellationToken ct)
    {
        var result = await workflow.ClaimAsync(User, id, ct);
        if (result == MutationResult.NotFound) return NotFound();
        SetFeedback(result);
        return result == MutationResult.Success ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Available));
    }
    [HttpPost, EnableRateLimiting("writes")] public Task<IActionResult> Accept(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.Pending, MaintenanceRequestStatus.Accepted, ct);
    [HttpPost, EnableRateLimiting("writes")] public Task<IActionResult> Start(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, ct);
    [HttpPost, EnableRateLimiting("writes")] public Task<IActionResult> Complete(int id, CancellationToken ct) => Transition(id, MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Completed, ct);
    private async Task<IActionResult> Transition(int id, MaintenanceRequestStatus from, MaintenanceRequestStatus to, CancellationToken ct)
    {
        var result = await workflow.TransitionAsync(User, id, from, to, ct);
        if (result == MutationResult.NotFound) return NotFound();
        SetFeedback(result);
        return RedirectToAction(nameof(Details), new { id });
    }
    private void SetFeedback(MutationResult result) => TempData[result == MutationResult.Success ? "SuccessMessage" : "ErrorMessage"] = result == MutationResult.Success
        ? "تم تحديث الطلب. قبولك يعني استعدادك للعمل؛ يبدأ التنفيذ بعد موافقة العميل على العرض."
        : "تعذر تنفيذ الخطوة. ربما حُجز الطلب أو تغيرت حالته؛ يلزم الاتفاق قبل البدء وتأكيد العميل للسعر النهائي قبل الإكمال.";
}
