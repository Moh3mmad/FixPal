using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FixPal.Controllers;
[Authorize, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("writes")]
public class DemoPaymentsController(RequestCommerceService commerce) : Controller
{
    [HttpPost]
    public async Task<IActionResult> ProposeFinal(int id, decimal finalPrice, CancellationToken ct)
    {
        var ok = ModelState.IsValid && (await commerce.ProposeFinalAsync(User, id, finalPrice, ct)) == MutationResult.Success;
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok ? "تم تسجيل السعر النهائي للمحاكاة بانتظار موافقة العميل." : "السعر يجب أن يكون ضمن النطاق المقبول وأثناء التنفيذ؛ لا يمكن استبدال سعر مسجل.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
    [HttpPost]
    public async Task<IActionResult> AcceptFinal(int id, CancellationToken ct)
    {
        var ok = (await commerce.AcceptFinalAsync(User, id, ct)) == MutationResult.Success;
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok ? "تم تأكيد السعر النهائي للمحاكاة. لم يتم دفع أموال." : "تعذر تأكيد السعر في الحالة الحالية.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id });
    }
}

