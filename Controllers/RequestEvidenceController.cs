using System.Data;
using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class RequestEvidenceController(ApplicationDbContext db, RequestAccessService access, IPrivateMediaStorage storage, ILogger<RequestEvidenceController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int id, int page = 1, CancellationToken ct = default)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant == null) return NotFound();
        return View(new EvidenceViewModel { RequestId = id, CanUpload = grant.CanParticipate,
            Evidence = await PagedResult<RequestEvidence>.CreateAsync(db.RequestEvidence.AsNoTracking().Where(e => e.MaintenanceRequestId == id).OrderByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id), page, ct) });
    }
    [HttpPost, EnableRateLimiting("writes"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, EvidenceKind kind, IFormFile? file, CancellationToken ct)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant is not { CanParticipate: true }) return NotFound();
        if (!Enum.IsDefined(kind) || file == null) { TempData["ErrorMessage"] = "اختر صورة ونوع توثيق صالحًا."; return RedirectToAction(nameof(Index), new { id }); }
        StoredMedia? saved = null;
        try
        {
            saved = await storage.SaveAsync(file, ct);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (await db.RequestEvidence.CountAsync(e => e.MaintenanceRequestId == id, ct) >= 20) throw new InvalidDataException("الحد الأقصى 20 صورة لكل طلب.");
            db.RequestEvidence.Add(new() { MaintenanceRequestId = id, UploadedById = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Kind = kind, StorageKey = saved.Key, ContentType = saved.ContentType, Size = saved.Size, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            TempData["SuccessMessage"] = "تمت إضافة الصورة الخاصة بالطلب.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or DbUpdateException)
        {
            if (saved != null) await storage.DeleteAsync(saved.Key, CancellationToken.None);
            logger.LogWarning(ex, "Evidence upload failed for request {Id}", id);
            TempData["ErrorMessage"] = ex is InvalidDataException ? ex.Message : "تعذر حفظ الصورة. حاول مجددًا.";
        }
        return RedirectToAction(nameof(Index), new { id });
    }
    [HttpGet]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        var evidence = await db.RequestEvidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (evidence == null || await access.GetAsync(User, evidence.MaintenanceRequestId, ct) == null) return NotFound();
        try { return File(await storage.OpenAsync(evidence.StorageKey, ct), evidence.ContentType); }
        catch (FileNotFoundException) { return NotFound(); }
    }
}
