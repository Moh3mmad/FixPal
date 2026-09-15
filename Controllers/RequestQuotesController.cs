using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[Authorize]
public class RequestQuotesController(RequestCommerceService commerce, ILogger<RequestQuotesController> logger) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Submit(int id, QuoteInputModel input, CancellationToken ct)
    {
        var ok = false;
        if (ModelState.IsValid)
        {
            try { ok = await commerce.SubmitQuoteAsync(User, id, input, ct); }
            catch (DbUpdateException ex) { logger.LogWarning(ex, "Concurrent quote submission for request {Id}", id); }
        }
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok ? "تم إرسال عرض السعر." : "تعذر إرسال العرض. تحقق من النطاق وحالة الطلب؛ يُسمح بعرض واحد فقط.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
    [HttpPost]
    public async Task<IActionResult> Accept(int id, CancellationToken ct)
    {
        var ok = await commerce.AcceptQuoteAsync(User, id, ct);
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok ? "تم قبول نطاق السعر. لا يتم دفع أي أموال في المنصة." : "تعذر قبول العرض؛ تحقق من ملكية الطلب وحالته.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
}
