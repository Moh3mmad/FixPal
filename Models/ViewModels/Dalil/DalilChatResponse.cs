using FixPal.Features.Dalil;

namespace FixPal.Models.ViewModels.Dalil;

public sealed record DalilChatResponse(
    bool Success,
    string Reply,
    string? SuggestedCategory = null,
    DalilSeverity? Severity = null,
    bool? SafetyRisk = null,
    bool? NeedsProfessional = null,
    string? NextStep = null,
    double? Confidence = null)
{
    public const string UnavailableMessage = "دليل غير متاح مؤقتًا. حاول مرة أخرى بعد قليل.";
    public const string InvalidMessage = "أرسل رسالة واضحة وقصيرة حتى يتمكن دليل من مساعدتك.";

    public static DalilChatResponse From(DalilAnswer answer) => new(
        true, answer.Reply, answer.SuggestedCategory, answer.Severity, answer.SafetyRisk,
        answer.NeedsProfessional, answer.NextStep, answer.Confidence);

    public static DalilChatResponse Unavailable() => new(false, UnavailableMessage);
    public static DalilChatResponse Invalid() => new(false, InvalidMessage);
}
