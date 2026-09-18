namespace FixPal.Features.Dalil;

public sealed class DalilAssistantOptions
{
    public const string SectionName = "Dalil";

    public string? GeminiApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 1024;
    public double Temperature { get; set; } = 0.2;
}
