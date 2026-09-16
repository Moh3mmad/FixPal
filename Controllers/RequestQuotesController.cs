using FixPal.Models;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace FixPal.Controllers;
[Authorize, EnableRateLimiting("writes")]
public class RequestQuotesController(RequestCommerceService commerce) : Controller
{
    [HttpPost]
    [FixPal.Infrastructure.RequireContactPhone]
    public async Task<IActionResult> Submit(int id, QuoteInputModel input, CancellationToken ct) =>
        Result(id, ModelState.IsValid ? await commerce.SubmitQuoteAsync(User, id, input, ct) : MutationResult.Conflict);
    [HttpPost]
    [FixPal.Infrastructure.RequireContactPhone]
    public Task<IActionResult> Accept(int id, int revisionNumber, CancellationToken ct) => Decide(id, revisionNumber, QuoteState.Accepted, null, ct);
    [HttpPost]
    public Task<IActionResult> Reject(int id, int revisionNumber, string? note, CancellationToken ct) => Decide(id, revisionNumber, QuoteState.Rejected, note, ct);
    [HttpPost]
    public Task<IActionResult> RequestRevision(int id, int revisionNumber, string? note, CancellationToken ct) => Decide(id, revisionNumber, QuoteState.RevisionRequested, note, ct);
    private async Task<IActionResult> Decide(int id, int revisionNumber, QuoteState state, string? note, CancellationToken ct) =>
        Result(id, ModelState.IsValid ? await commerce.DecideAsync(User, id, revisionNumber, state, note, ct) : MutationResult.Conflict);
    private IActionResult Result(int id, MutationResult result)
    {
        if (result == MutationResult.NotFound) return NotFound();
        TempData[result == MutationResult.Success ? "SuccessMessage" : "ErrorMessage"] = result == MutationResult.Success
            ? "تم حفظ قرارك أو عرضك في سجل الاتفاق. لا تُجرى أي دفعات حقيقية."
            : "لم يُحفظ التغيير. ربما تغير العرض أو حالة الطلب؛ حدّث الصفحة وراجع البيانات قبل المحاولة.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
}
