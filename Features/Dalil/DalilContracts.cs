using System.Text.Json.Serialization;

namespace FixPal.Features.Dalil;

public enum DalilChatRole
{
    User,
    Assistant
}

public enum DalilSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum DalilOutcome
{
    Success,
    Unavailable
}

public sealed record DalilChatMessage(DalilChatRole Role, string Content);

public sealed record DalilPublicServiceCategory(string Name, string? Description = null);

public sealed record DalilPublicLocation(string City, string? Area);

public sealed record DalilSafeContext(
    IReadOnlyList<DalilPublicServiceCategory>? ServiceCategories = null,
    IReadOnlyList<DalilPublicLocation>? Locations = null);

public sealed record DalilAssistantRequest(
    IReadOnlyList<DalilChatMessage> Messages,
    DalilSafeContext? Context = null);

public sealed record DalilAnswer(
    string Reply,
    string? SuggestedCategory = null,
    string? SuggestedCity = null,
    string? SuggestedArea = null,
    DalilSeverity? Severity = null,
    bool? SafetyRisk = null,
    bool? NeedsProfessional = null,
    string? NextStep = null,
    double? Confidence = null);

public sealed record DalilAssistantResult(DalilOutcome Outcome, DalilAnswer? Answer)
{
    public static DalilAssistantResult Success(DalilAnswer answer) => new(DalilOutcome.Success, answer);
    public static DalilAssistantResult Unavailable() => new(DalilOutcome.Unavailable, null);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DalilProviderMatchScope
{
    ExactArea,
    SameCity
}

public sealed record DalilProviderRecommendation(
    int Id,
    string DisplayName,
    string Specialty,
    string City,
    string Area,
    double? AverageRating,
    int ReviewCount,
    string AvailabilityLabel,
    DalilProviderMatchScope MatchScope,
    string? ProfileUrl,
    string? RequestUrl);
