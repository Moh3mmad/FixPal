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

public sealed record DalilPublicServiceCategory(string Name);

public sealed record DalilPublicLocation(string City, string? Area);

public sealed record DalilPublicProviderSummary(
    string DisplayName,
    string? Specialty,
    string? Area,
    string? AvailabilitySummary);

public sealed record DalilSafeContext(
    IReadOnlyList<DalilPublicServiceCategory>? ServiceCategories = null,
    IReadOnlyList<DalilPublicLocation>? Locations = null,
    IReadOnlyList<DalilPublicProviderSummary>? Providers = null,
    IReadOnlyList<string>? HelpItems = null);

public sealed record DalilAssistantRequest(
    IReadOnlyList<DalilChatMessage> Messages,
    DalilSafeContext? Context = null);

public sealed record DalilAnswer(
    string Reply,
    string? SuggestedCategory = null,
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
