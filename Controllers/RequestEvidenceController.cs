using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace FixPal.Controllers;
[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class RequestEvidenceController(ApplicationDbContext db, RequestAccessService access, IPrivateMediaStorage storage,
    ILogger<RequestEvidenceController> logger, RequestEvidencePolicy policy, RequestMutationService mutations) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int id, int page = 1, CancellationToken ct = default)
    {
        var grant = await access.GetAsync(User, id, ct);
        if (grant == null) return NotFound();
        return View(new EvidenceViewModel { RequestId = id, UploadKind = await policy.UploadKindAsync(grant, ct),
            Evidence = await PagedResult<RequestEvidence>.CreateAsync(db.RequestEvidence.AsNoTracking().Where(e => e.MaintenanceRequestId == id).OrderByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id), page, ct) });
    }
    [HttpPost, EnableRateLimiting("writes"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
    int id,
    EvidenceKind kind,
    IFormFile? file,
    CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        var grant = await access.GetAsync(User, id, ct);

        if (kind is not (EvidenceKind.Before or EvidenceKind.After)
            || await policy.UploadKindAsync(grant, ct) != kind)
        {
            return NotFound();
        }

        if (file == null)
        {
            TempData["ErrorMessage"] = "اختر صورة صالحة.";
            return RedirectToAction(nameof(Index), new { id });
        }

        StoredMedia? saved = null;
        var committed = false;

        try
        {
            // External media IO remains outside the SQL execution strategy.
            saved = await storage.SaveAsync(file, ct);

            var result = await mutations.RunAsync(
                id,
                async () =>
                {
                    var current = await access.GetAsync(User, id, ct);

                    if (await policy.UploadKindAsync(current, ct) != kind)
                        return MutationResult.Conflict;

                    if (await db.RequestEvidence.CountAsync(
                            e => e.MaintenanceRequestId == id,
                            ct) >= 20)
                    {
                        throw new InvalidDataException(
                            "الحد الأقصى 20 صورة لكل طلب.");
                    }

                    db.RequestEvidence.Add(new RequestEvidence
                    {
                        MaintenanceRequestId = id,
                        UploadedById = User.FindFirstValue(
                            ClaimTypes.NameIdentifier)!,
                        Kind = kind,
                        OwnershipChecked = true,
                        StorageKey = saved.Key,
                        ContentType = saved.ContentType,
                        Size = saved.Size,
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(ct);

                    return MutationResult.Success;
                },
                async verifyCt =>
                    await db.RequestEvidence
                        .AsNoTracking()
                        .AnyAsync(
                            e => e.MaintenanceRequestId == id
                                 && e.StorageKey == saved.Key,
                            verifyCt),
                ct);

            committed = result == MutationResult.Success;

            TempData[committed
                ? "SuccessMessage"
                : "ErrorMessage"] =
                committed
                    ? "تمت إضافة الصورة الخاصة بالطلب."
                    : "تغيرت حالة الطلب أو صلاحية الرفع. حدّث الصفحة وراجع البيانات.";
        }
        catch (Exception ex) when (
            ex is InvalidDataException
                or IOException
                or DbUpdateException)
        {
            logger.LogWarning(
                ex,
                "Evidence upload failed for request {Id}",
                id);

            TempData["ErrorMessage"] =
                ex is InvalidDataException
                    ? ex.Message
                    : "تعذر حفظ الصورة. حاول مجددًا.";
        }
        finally
        {
            if (saved != null && !committed)
            {
                // Never delete media when SQL's final commit state is unknown.
                // First check whether the DB references this exact storage key.
                var referenced = true;

                try
                {
                    db.ChangeTracker.Clear();

                    referenced = await db.RequestEvidence
                        .AsNoTracking()
                        .AnyAsync(
                            e => e.MaintenanceRequestId == id
                                 && e.StorageKey == saved.Key,
                            CancellationToken.None);
                }
                catch (Exception verificationEx)
                {
                    // Preserving a possible orphan is safer than deleting media
                    // that a successful-but-unacknowledged transaction references.
                    logger.LogWarning(
                        verificationEx,
                        "Could not verify evidence media reference for {StorageKey}; media was preserved.",
                        saved.Key);
                }

                if (!referenced)
                {
                    try
                    {
                        await storage.DeleteAsync(
                            saved.Key,
                            CancellationToken.None);
                    }
                    catch (Exception cleanupEx)
                    {
                        logger.LogWarning(
                            cleanupEx,
                            "Could not clean up evidence media {StorageKey}.",
                            saved.Key);
                    }
                }
            }
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
