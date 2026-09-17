using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FixPal.Controllers;

[Authorize]
public sealed class AppointmentsController(AppointmentService appointments) : Controller
{
    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Schedule(ScheduleAppointmentCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput(command.MaintenanceRequestId);
        var result = await appointments.ScheduleAsync(User, command, ct);
        return MapResult(result, command.MaintenanceRequestId, "تم إرسال اقتراح الموعد للطرف الآخر.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Reschedule(RescheduleAppointmentCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput(command.MaintenanceRequestId);
        var result = await appointments.RescheduleAsync(User, command, ct);
        return MapResult(result, command.MaintenanceRequestId, "تم إرسال اقتراح الموعد البديل للطرف الآخر.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Confirm(AppointmentDecisionCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput(command.MaintenanceRequestId);
        var result = await appointments.ConfirmAsync(User, command, ct);
        return MapResult(result, command.MaintenanceRequestId, "تم تأكيد الموعد.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Reject(AppointmentDecisionCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput(command.MaintenanceRequestId);
        var result = await appointments.RejectAsync(User, command, ct);
        return MapResult(result, command.MaintenanceRequestId, "تم رفض اقتراح الموعد.");
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Cancel(CancelAppointmentCommand command, CancellationToken ct)
    {
        if (!ModelState.IsValid) return InvalidInput(command.MaintenanceRequestId);
        var result = await appointments.CancelAsync(User, command, ct);
        return MapResult(result, command.MaintenanceRequestId, "تم إلغاء الموعد أو سحب الاقتراح.");
    }

    private IActionResult MapResult(AppointmentResult result, int requestId, string successMessage)
    {
        switch (result.Status)
        {
            case AppointmentResultStatus.Success:
                TempData["SuccessMessage"] = successMessage;
                break;
            case AppointmentResultStatus.NotFound:
                return NotFound();
            case AppointmentResultStatus.Forbidden:
                return Forbid();
            case AppointmentResultStatus.ValidationFailed:
                TempData["ErrorMessage"] = "تعذر تنفيذ العملية بسبب بيانات موعد غير صالحة.";
                break;
            case AppointmentResultStatus.Conflict:
                TempData["ErrorMessage"] = "تعذر تنفيذ العملية لأن بيانات الموعد تغيرت أو تتعارض مع الحجز الحالي. حدّث الصفحة وراجع البيانات.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null);
        }
        return RedirectToAction("Details", "MaintenanceRequests", new { id = requestId });
    }

    private IActionResult InvalidInput(int requestId)
    {
        TempData["ErrorMessage"] = "تعذر تنفيذ العملية بسبب بيانات موعد غير صالحة.";
        return RedirectToAction("Details", "MaintenanceRequests", new { id = requestId });
    }
}
