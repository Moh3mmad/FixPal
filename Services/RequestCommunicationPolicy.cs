using System.Security.Claims;
using FixPal.Data;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;

public class RequestCommunicationPolicy(ApplicationDbContext db, RequestAgreementPolicy agreement, AccountPhoneService phones)
{
    // The caller must obtain this grant through RequestAccessService first.
    public async Task<CommunicationState> GetAsync(RequestAccess grant, ClaimsPrincipal user, CancellationToken ct)
    {
        var r = grant.Request;
        if (r.RequestType != RequestType.PrivateService) return new();
        var agreed = await agreement.AgreedRequests.AnyAsync(x => x.Id == r.Id, ct);
        var active = await agreement.CanMessageAsync(r.Id, ct);
        var needsPhone = grant.CanParticipate && !await phones.HasUsableAsync(user);
        var completed = agreed && r.Status == MaintenanceRequestStatus.Completed && r.CompletedAtUtc != null;
        var inspection = !grant.CanParticipate;
        // Legacy history remains readable under the existing request authorization,
        // but historical status never supplies missing agreement evidence.
        var canRead = inspection || agreed || r.IsLegacy;
        string? contact = null;
        if (grant.CanParticipate && agreed && r.Status != MaintenanceRequestStatus.Cancelled)
        {
            var otherId = grant.IsOwner
                ? await db.ProviderProfiles.Where(p => p.Id == r.ProviderProfileId).Select(p => p.UserId).SingleAsync(ct)
                : r.CustomerId;
            var raw = await db.Users.Where(u => u.Id == otherId).Select(u => u.PhoneNumber).SingleOrDefaultAsync(ct);
            if (phones.TryNormalize(raw, out var normalized)) contact = normalized;
        }
        return new()
        {
            IsPrivate = true, CanReadHistory = canRead, CanSend = grant.CanParticipate && active && !needsPhone,
            NeedsPhone = needsPhone && active,
            Heading = inspection ? "عرض إداري للقراءة فقط" : completed ? "اكتملت الخدمة · المحادثة للقراءة فقط" : active ? needsPhone ? "تم الاتفاق · أضف رقم هاتفك" : "تم الاتفاق · التواصل متاح" : "المحادثة مقفلة",
            Explanation = inspection ? "يمكنك مراجعة سجل المحادثة دون إرسال رسائل."
                : completed ? "اكتملت الخدمة. يمكنك مراجعة المحادثة السابقة، لكن إرسال رسائل جديدة متوقف."
                : r.Status == MaintenanceRequestStatus.Cancelled ? "أُلغي الطلب. إرسال رسائل جديدة متوقف."
                : active ? needsPhone ? "أضف رقم هاتفك إلى حسابك لتتمكن من إرسال الرسائل." : "يمكنك الآن التواصل مع الطرف الآخر ومتابعة تفاصيل الخدمة."
                : agreed ? "إرسال الرسائل متوقف حاليًا لأن مزود الخدمة غير متاح للعمل."
                : "المحادثة متاحة بعد قبول عرض محدد والاتفاق عليه. سجل الرسائل السابق، إن وجد، لا يعني تأكيد الاتفاق.",
            NextAction = completed ? "يمكن لصاحب الطلب تقييم الخدمة من تفاصيل الطلب."
                : r.Status == MaintenanceRequestStatus.Cancelled ? "يمكنك الرجوع إلى سجل الطلب."
                : !agreed ? r.ProviderProfileId == null ? "بانتظار تعيين مزود وتقديم عرض." : "راجع قسم العرض لإتمام الاتفاق."
                : !active ? "يمكنك مراجعة سجل الطلب والمحادثة السابقة."
                : r.Status == MaintenanceRequestStatus.InProgress ? "بعد إنجاز العمل، يؤكد العميل السعر النهائي ثم يؤكد المزود الإكمال."
                : "يمكن للمزود بدء التنفيذ من تفاصيل الطلب.",
            ContactPhone = contact, ContactLabel = grant.IsOwner ? "هاتف مزود الخدمة" : "هاتف صاحب الطلب",
            ContactExplanation = inspection ? "أرقام الهواتف غير معروضة في سجل المراجعة."
                : !agreed ? "تظهر معلومات التواصل بعد قبول العرض والاتفاق."
                : r.Status == MaintenanceRequestStatus.Cancelled ? "معلومات الاتصال غير متاحة لهذا الطلب الملغي."
                : contact == null ? "لم يضف الطرف الآخر رقم هاتف صالحًا بعد. يمكنك الرجوع إلى المحادثة."
                : "هذا الرقم متاح لك بصفتك طرفًا في الطلب. لم يتم التحقق منه برسالة نصية."
        };
    }
}
