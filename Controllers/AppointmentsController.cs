using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FixPal.Controllers;

[Authorize]
public sealed class AppointmentsController(AppointmentService appointments, ILogger<AppointmentsController> logger) : Controller
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
        if (result.Status != AppointmentResultStatus.Success)
            logger.LogWarning("Appointment action {Action} rejected for request {RequestId}: {Status}; codes {Codes}",
                ControllerContext.ActionDescriptor.ActionName, requestId, result.Status,
                string.Join(",", result.Errors.Select(e => e.Code).Distinct()));
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
            case AppointmentResultStatus.Conflict:
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => ErrorMessage(e.Code)).Distinct());
                if (result.Errors.Count == 0) TempData["ErrorMessage"] = ErrorMessage(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null);
        }
        return RedirectToAction("Details", "MaintenanceRequests", new { id = requestId });
    }

    private IActionResult InvalidInput(int requestId)
    {
        // Do not log raw submitted values, exception text or model-binding errors.
        logger.LogWarning(
            "Appointment action {Action} rejected for request {RequestId}: InvalidFormInput",
            ControllerContext.ActionDescriptor.ActionName,
            requestId);

        TempData["ErrorMessage"] =
            "تعذر قراءة بيانات الموعد. أدخل تاريخًا ووقتًا صالحين للبداية والنهاية حسب توقيت تقويم مزود الخدمة، ثم حاول مجددًا.";

        return RedirectToAction(
            "Details",
            "MaintenanceRequests",
            new { id = requestId });
    }

    private static string ErrorMessage(string? code) => code switch
    {
        "StartMustBeFuture" =>
            "يجب اختيار موعد في المستقبل.",

        "OutsideWorkingHours" =>
            "الموعد المختار خارج ساعات عمل مزود الخدمة.",

        "InvalidInterval" =>
            "يجب أن يكون وقت نهاية الموعد بعد وقت البداية.",

        "BlackoutConflict" =>
            "مزود الخدمة غير متاح خلال هذه الفترة. اختر وقتًا آخر.",

        "ProviderTimeConflict" =>
            "هذا الوقت محجوز أو غير متاح. اختر وقتًا آخر.",

        "CalendarDisabled" =>
            "مزود الخدمة لا يستقبل مواعيد جديدة حاليًا.",

        "CalendarMissing" =>
            "لم يُعِدّ مزود الخدمة تقويم العمل بعد.",

        "InvalidDateTimeKind" =>
            "أدخل الوقت المحلي حسب تقويم مزود الخدمة دون تحويله إلى توقيت عالمي أو إضافة فرق توقيت.",

        "MinutePrecisionRequired" =>
            "اختر الوقت بالساعات والدقائق فقط، دون ثوانٍ أو أجزاء من الثانية.",

        "NonexistentLocalTime" or "AmbiguousLocalTime" =>
            "الوقت المختار غير صالح أو ملتبس بسبب تغيير التوقيت الصيفي. اختر وقتًا آخر.",

        "CalendarTimeZoneUnavailable" or "UtcConversionOutOfRange" =>
            "تعذر تحويل الوقت حسب تقويم مزود الخدمة. راجع إعدادات المنطقة الزمنية واختر تاريخًا صالحًا.",

        "AgreementRequired" =>
            "يجب قبول اتفاق الخدمة قبل اقتراح موعد.",

        "ProviderNotEligible" =>
            "مزود الخدمة غير متاح لحجز هذا الطلب حاليًا.",

        "RequestNotSchedulable" =>
            "حالة الطلب الحالية لا تسمح بتغيير الموعد.",

        "RequestAlreadyScheduled" =>
            "يوجد موعد مؤكد لهذا الطلب. استخدم اقتراح موعد بديل.",

        _ =>
            "تعذر تنفيذ العملية لأن بيانات الموعد تغيرت أو غير صالحة. حدّث الصفحة وراجع البيانات ثم حاول مجددًا."
    };
}
