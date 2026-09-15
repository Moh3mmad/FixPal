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
public class RequestMessagesController(ApplicationDbContext db, RequestAccessService access) : Controller
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
        return View(new RequestMessagesViewModel { RequestId = id, Title = grant.Request.Title, CanSend = grant.CanParticipate,
            Messages = await PagedResult<MessageItem>.CreateAsync(query, page, ct, 30) });
    }
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Send(int id, string? text, CancellationToken ct)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant is not { CanParticipate: true }) return NotFound();
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000) TempData["ErrorMessage"] = "الرسالة مطلوبة وبحد أقصى 2000 حرف.";
        else
        {
            db.RequestMessages.Add(new() { MaintenanceRequestId = id, SenderId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Text = text, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Index), new { id });
    }
}
