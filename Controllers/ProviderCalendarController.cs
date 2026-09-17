using FixPal.Infrastructure.Identity;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FixPal.Controllers;

[Authorize(Roles = AppRoles.Provider)]
public sealed class ProviderCalendarController(
    ProviderCalendarService calendars,
    ProviderBlackoutService blackouts) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var calendar = await calendars.GetOwnAsync(User, ct);
        var blackoutManagement = calendar == null ? null : await blackouts.GetOwnAsync(User, ct);
        return View(new ProviderCalendarPageViewModel(calendar, blackoutManagement));
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Create(CreateProviderCalendarCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput();
        var result = await calendars.CreateAsync(User, command, ct);
        return CalendarResult(result, "تم إنشاء تقويم العمل بنجاح.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Update(UpdateProviderCalendarCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput();
        var result = await calendars.UpdateAsync(User, command, ct);
        return CalendarResult(result, "تم تحديث تقويم العمل بنجاح.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> CreateBlackout(CreateProviderBlackoutCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput();
        var result = await blackouts.CreateAsync(User, command, ct);
        return BlackoutResult(result, "تمت إضافة فترة عدم التوفر بنجاح.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> RemoveBlackout(RemoveProviderBlackoutCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput();
        var result = await blackouts.RemoveAsync(User, command, ct);
        return BlackoutResult(result, "تمت إزالة فترة عدم التوفر بنجاح.");
    }

    private IActionResult CalendarResult(ProviderCalendarResult result, string successMessage) => result.Status switch
    {
        ProviderCalendarStatus.Success => Success(successMessage),
        ProviderCalendarStatus.Forbidden => Forbid(),
        ProviderCalendarStatus.NotFound => NotFound(),
        ProviderCalendarStatus.ValidationFailed or ProviderCalendarStatus.Stale or ProviderCalendarStatus.Conflict
            => CalendarError(),
        _ => throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null)
    };

    private IActionResult BlackoutResult(ProviderBlackoutResult result, string successMessage) => result.Status switch
    {
        ProviderBlackoutStatus.Success => Success(successMessage),
        ProviderBlackoutStatus.Forbidden => Forbid(),
        ProviderBlackoutStatus.NotFound => NotFound(),
        ProviderBlackoutStatus.ValidationFailed or ProviderBlackoutStatus.Stale or ProviderBlackoutStatus.Conflict
            => BlackoutError(),
        _ => throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null)
    };

    private IActionResult Success(string message)
    {
        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    private IActionResult CalendarError()
    {
        TempData["ErrorMessage"] = "تعذر حفظ تقويم العمل. راجع البيانات وحدّث الصفحة ثم حاول مجددًا.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult BlackoutError()
    {
        TempData["ErrorMessage"] = "تعذر تحديث فترة عدم التوفر. راجع البيانات وحدّث الصفحة ثم حاول مجددًا.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult InvalidInput()
    {
        TempData["ErrorMessage"] = "تعذر تنفيذ العملية بسبب بيانات إدخال غير صالحة.";
        return RedirectToAction(nameof(Index));
    }
}
