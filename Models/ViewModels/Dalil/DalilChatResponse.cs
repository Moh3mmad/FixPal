using FixPal.Features.Dalil;
using System.Globalization;

namespace FixPal.Models.ViewModels.Dalil;

public sealed record DalilChatResponse(
    bool Success,
    string Reply,
    string HistoryReply,
    string? SuggestedCategory = null,
    string? SuggestedCity = null,
    string? SuggestedArea = null,
    DalilSeverity? Severity = null,
    bool? SafetyRisk = null,
    bool? NeedsProfessional = null,
    string? NextStep = null,
    double? Confidence = null,
    bool LocationRequired = false,
    IReadOnlyList<DalilProviderRecommendation>? Providers = null)
{
    public const string UnavailableMessage = "دليل غير متاح مؤقتًا. حاول مرة أخرى بعد قليل.";
    public const string InvalidMessage = "أرسل رسالة واضحة وقصيرة حتى يتمكن دليل من مساعدتك.";

    public static DalilChatResponse From(
        DalilAnswer answer,
        string? category,
        string? city,
        string? area,
        bool locationRequired,
        IReadOnlyList<DalilProviderRecommendation> providers) => new(
        true, AppendProviderSummary(answer.Reply, providers), answer.Reply, category, city, area, answer.Severity, answer.SafetyRisk,
        answer.NeedsProfessional, answer.NextStep, answer.Confidence, locationRequired, providers);

    private static string AppendProviderSummary(
        string reply,
        IReadOnlyList<DalilProviderRecommendation> providers)
    {
        if (providers.Count == 0) return reply;
        var sameCity = providers[0].MatchScope == DalilProviderMatchScope.SameCity;
        var lines = providers.Take(3).Select(provider =>
        {
            var rating = provider.AverageRating.HasValue
                ? $" — {provider.AverageRating.Value.ToString("0.0", CultureInfo.InvariantCulture)}/5"
                : string.Empty;
            var area = provider.MatchScope == DalilProviderMatchScope.SameCity
                ? $" — {provider.Area}"
                : string.Empty;
            return $"- {provider.DisplayName} — {provider.Specialty}{area}{rating}";
        });
        var heading = sameCity
            ? "لم أجد مزود خدمة مطابقًا داخل منطقتك، لكن وجدت لك مزودي خدمة من نفس المدينة:"
            : "وجدت لك مزودي خدمة مناسبين على تصليحة ضمن منطقتك:";
        return $"{reply.TrimEnd()}\n\n{heading}\n\n{string.Join("\n", lines)}";
    }

    public static DalilChatResponse Unavailable() => new(false, UnavailableMessage, string.Empty);
    public static DalilChatResponse Invalid() => new(false, InvalidMessage, string.Empty);
}
