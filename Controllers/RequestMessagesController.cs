using System.Security.Claims;
using FixPal.Data;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class RequestMessagesController(ApplicationDbContext db, RequestAccessService access, RequestAgreementPolicy agreement, RequestMutationService mutations) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int id, int page = 1, CancellationToken ct = default)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.RequestMessages.AsNoTracking().Where(m => m.MaintenanceRequestId == id)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Select(m => new MessageItem(m.Id, m.Text, m.CreatedAtUtc, m.SenderId == userId, m.SenderId == grant.Request.CustomerId));
        return View(new RequestMessagesViewModel { RequestId = id, Title = grant.Request.Title, CanSend = grant.CanParticipate && await agreement.CanMessageAsync(id, ct),
            Messages = await PagedResult<MessageItem>.CreateAsync(query, page, ct, 30) });
    }
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Send(int id, string? text, CancellationToken ct)
    {
        var result = await mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(User, id, ct);
            if (grant is not { CanParticipate: true }) return MutationResult.NotFound;
            text = text?.Trim();
            if (!ModelState.IsValid || string.IsNullOrEmpty(text) || text.Length > 2000 || !await agreement.CanMessageAsync(id, ct))
                return MutationResult.Conflict;
            db.RequestMessages.Add(new() { MaintenanceRequestId = id, SenderId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Text = text, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);
        if (result == MutationResult.NotFound) return NotFound();
        if (result == MutationResult.Conflict) TempData["ErrorMessage"] = "تتاح المراسلة بعد الاتفاق وأثناء الطلب النشط فقط. الرسالة حتى 2000 حرف.";
        return RedirectToAction(nameof(Index), new { id });
    }
}
