using FixPal.Data;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize]
public class ProviderReviewsController(ApplicationDbContext db, RequestAccessService access, RequestAgreementPolicy agreement, RequestMutationService mutations) : Controller
{
    [HttpPost, EnableRateLimiting("writes")]
    public async Task<IActionResult> Create(int id, int rating, string? comment, CancellationToken ct)
    {
        comment = comment?.Trim();
        var result = await mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(User, id, ct);
            if (grant is not { IsOwner: true }) return MutationResult.NotFound;
            if (!ModelState.IsValid || rating is < 1 or > 5 || comment?.Length > 1000 || !await agreement.CanReviewAsync(id, ct)
                || await db.ProviderReviews.AnyAsync(r => r.MaintenanceRequestId == id, ct)) return MutationResult.Conflict;
            db.ProviderReviews.Add(new() { MaintenanceRequestId = id, ProviderProfileId = grant.Request.ProviderProfileId!.Value,
                CustomerId = grant.Request.CustomerId, Rating = rating, Comment = comment, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);
        if (result == MutationResult.NotFound) return NotFound();
        TempData[result == MutationResult.Success ? "SuccessMessage" : "ErrorMessage"] = result == MutationResult.Success
            ? "شكرًا. نُشر تقييمك دون عرض اسمك أو تفاصيل طلبك." : "التقييم متاح مرة واحدة بعد خدمة مكتملة باتفاق الطرفين وتأكيد السعر النهائي.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
}
