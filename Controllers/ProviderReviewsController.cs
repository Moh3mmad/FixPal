using System.Data;
using FixPal.Data;
using FixPal.Models.Enums;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize]
public class ProviderReviewsController(ApplicationDbContext db, RequestAccessService access) : Controller
{
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Create(int id, int rating, string? comment, CancellationToken ct)
    {
        comment = comment?.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grant = await access.GetAsync(User, id, ct);
        if (grant is not { IsOwner: true }) return NotFound();
        if (!ModelState.IsValid || rating is < 1 or > 5 || comment?.Length > 1000 || grant.Request.Status != MaintenanceRequestStatus.Completed || grant.Request.ProviderProfileId == null
            || await db.ProviderReviews.AnyAsync(r => r.MaintenanceRequestId == id, ct))
        { TempData["ErrorMessage"] = "التقييم من 1 إلى 5، مرة واحدة بعد اكتمال الطلب."; return RedirectToAction("Details", "MaintenanceRequests", new { id }); }
        db.ProviderReviews.Add(new() { MaintenanceRequestId = id, ProviderProfileId = grant.Request.ProviderProfileId.Value, CustomerId = grant.Request.CustomerId,
            Rating = rating, Comment = comment, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        TempData["SuccessMessage"] = "شكرًا. نُشر تقييمك دون عرض اسمك أو تفاصيل طلبك.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
}
