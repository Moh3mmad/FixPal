using FixPal.Models.Enums;

namespace FixPal.Models.ViewModels;

// Presentation guidance only. Links lead to existing actions whose authorization
// and prerequisites are still enforced by their services.
public sealed record RequestNextStepViewModel(string Title, string Description, string Target, string LinkText)
{
    public static RequestNextStepViewModel From(RequestDetailsViewModel request)
    {
        if (request.RequestType != RequestType.PrivateService)
            return new("متابعة البلاغ", "هذا بلاغ داخلي، وليس طلبًا موجّهًا إلى جهة رسمية أو قناة طوارئ.", "#request-description", "عرض تفاصيل البلاغ");
        if (!request.IsOwner && !request.CanManage)
            return new("متابعة حالة الطلب", "راجع حالة الطلب والاتفاق المسجّل أدناه.", "#request-tracking", "عرض سير الطلب");
        if (request.Status == MaintenanceRequestStatus.Cancelled)
            return new("الطلب ملغي", "تبقى تفاصيله وتاريخه محفوظة للرجوع إليها.", "#request-tracking", "عرض السجل");
        if (request.Status == MaintenanceRequestStatus.Completed)
            return request.CanReview
                ? new("كيف كانت تجربتك؟", "اكتمل العمل. شارك تقييمك للخدمة من قسم التقييم أدناه.", "#request-review", "تقييم التجربة")
                : new("اكتمل العمل", "يمكنك مراجعة صور العمل والاتفاق وسجل التنفيذ.", "#request-tracking", "عرض سير الطلب");
        if (request.Status == MaintenanceRequestStatus.Pending)
            return new(request.CanManage ? "راجع الطلب قبل قبوله" : "بانتظار قبول الطلب",
                request.ProviderName == null ? "لم يُعيّن مزود بعد. يبقى الطلب متاحًا لمزود مؤهل لتولّيه."
                    : "يراجع المزود التفاصيل قبل الاتفاق على العمل.", "#request-workflow", "عرض حالة التنفيذ");
        if (!request.HasAgreement)
            return request.CanDecideQuote && request.Quote?.State == QuoteState.AwaitingCustomer
                ? new("راجع عرض السعر", "اقرأ التفاصيل، ثم اقبل العرض أو اطلب تعديله.", "#request-quote", "مراجعة العرض")
                : new("الاتفاق على السعر أولًا", "راجع قسم عرض السعر لمعرفة العرض الحالي أو الخطوة المطلوبة من الطرف الآخر.", "#request-quote", "عرض الاتفاق");
        if (request.Status == MaintenanceRequestStatus.Accepted)
            return new("رتّبا الموعد، ثم تابعا التنفيذ", "راجع قسم موعد الزيارة للحجز أو لمراجعة موعدك الحالي. يحدّث المزود حالة الطلب عند بدء العمل فعليًا.",
                "#appointment-heading", "عرض موعد الزيارة");
        if (request.Quote?.FinalPriceAcceptedAtUtc == null)
            return new(request.Quote?.FinalPrice == null ? "العمل جارٍ — بانتظار السعر النهائي" : "بانتظار تأكيد السعر النهائي",
                "راجع قسم السعر النهائي. هذه محاكاة فقط؛ لا تُحصّل أموال داخل المنصة.", "#request-price", "مراجعة السعر النهائي");
        return new(request.CanManage ? "تأكيد اكتمال العمل" : "بانتظار تأكيد الإنجاز من المزود",
            "تم تأكيد السعر النهائي. يُسجّل اكتمال الطلب بعد إنجاز العمل فعليًا.", "#request-workflow", "عرض حالة التنفيذ");
    }
}
