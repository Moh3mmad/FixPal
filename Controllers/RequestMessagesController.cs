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
public class RequestMessagesController(ApplicationDbContext db, RequestAccessService access, RequestCommunicationPolicy communication, RequestMutationService mutations) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int id, int page = 1, CancellationToken ct = default)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant == null) return NotFound();
        var state = await communication.GetAsync(grant, User, ct);
        if (!state.IsPrivate) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.RequestMessages.AsNoTracking().Where(m => m.MaintenanceRequestId == id)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Select(m => new MessageItem(m.Id, m.Text, m.CreatedAtUtc, m.SenderId == userId, m.SenderId == grant.Request.CustomerId));
        return View(new RequestMessagesViewModel { RequestId = id, Title = grant.Request.Title, Communication = state,
            Messages = state.CanReadHistory ? await PagedResult<MessageItem>.CreateAsync(query, page, ct, 30) : new() { Page = 1 } });
    }
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Send(int id, string? text, CancellationToken ct)
    {
        var feedback = "تغيرت حالة الطلب. حدّث المحادثة ثم حاول مجددًا.";
        var result = await mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(User, id, ct);
            if (grant is not { CanParticipate: true }) return MutationResult.NotFound;
            text = text?.Trim();
            var state = await communication.GetAsync(grant, User, ct);
            if (!state.IsPrivate) return MutationResult.NotFound;
            if (!state.CanSend) { feedback = state.Explanation; return MutationResult.Conflict; }
            if (!ModelState.IsValid || string.IsNullOrEmpty(text) || text.Length > 2000)
            { feedback = "اكتب رسالة من حرف واحد إلى 2000 حرف."; return MutationResult.Conflict; }
            db.RequestMessages.Add(new() { MaintenanceRequestId = id, SenderId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Text = text, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);
        if (result == MutationResult.NotFound) return NotFound();
        if (result == MutationResult.Conflict) TempData["ErrorMessage"] = feedback;
        return RedirectToAction(nameof(Index), new { id });
    }
}
