namespace FixPal.Models.ViewModels;

public record CommunicationState
{
    public bool IsPrivate { get; init; }
    public bool CanReadHistory { get; init; }
    public bool CanSend { get; init; }
    public bool NeedsPhone { get; init; }
    public string Heading { get; init; } = "التواصل غير متاح";
    public string Explanation { get; init; } = "المحادثة مخصصة لطلبات الصيانة الخاصة.";
    public string NextAction { get; init; } = string.Empty;
    public string ContactExplanation { get; init; } = "تظهر معلومات التواصل بعد قبول العرض والاتفاق.";
    public string? ContactPhone { get; init; }
    public string ContactLabel { get; init; } = string.Empty;
}
