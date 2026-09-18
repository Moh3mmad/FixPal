using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FixPal.Features.Dalil;

public sealed class GeminiDalilAssistantService(
    HttpClient http,
    IOptions<DalilAssistantOptions> options,
    ILogger<GeminiDalilAssistantService> logger) : IDalilAssistantService
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DalilAssistantResult> RespondAsync(
        DalilAssistantRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsBounded(request)) return DalilAssistantResult.Unavailable();
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.GeminiApiKey)
            || string.IsNullOrWhiteSpace(settings.Model)
            || !TryEndpoint(settings, out var endpoint))
            return DalilAssistantResult.Unavailable();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 120)));

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.Add("x-goog-api-key", settings.GeminiApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Content = JsonContent.Create(BuildProviderRequest(request, settings));

            using var response = await http.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Dalil provider returned HTTP {StatusCode}", (int)response.StatusCode);
                return DalilAssistantResult.Unavailable();
            }

            var payload = await ReadLimitedAsync(response.Content, timeout.Token);
            if (payload == null) return DalilAssistantResult.Unavailable();
            return ParseProviderResponse(payload, request);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Dalil provider timed out");
            return DalilAssistantResult.Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or IOException or InvalidOperationException)
        {
            logger.LogWarning("Dalil provider unavailable ({FailureType})", exception.GetType().Name);
            return DalilAssistantResult.Unavailable();
        }
    }

    private static object BuildProviderRequest(DalilAssistantRequest request, DalilAssistantOptions settings)
    {
        var contents = request.Messages.Select(message => new
        {
            role = message.Role == DalilChatRole.User ? "user" : "model",
            parts = new[] { new { text = message.Content } }
        }).ToList<object>();

        if (request.Context != null)
        {
            contents.Insert(0, new
            {
                role = "user",
                parts = new[] { new { text = "سياق FixPal عام وآمن، استخدمه كمرجع فقط:\n" + JsonSerializer.Serialize(request.Context, Json) } }
            });
        }

        return new
        {
            systemInstruction = new { parts = new[] { new { text = DalilSystemPrompt.Text } } },
            contents,
            generationConfig = new
            {
                temperature = Math.Clamp(settings.Temperature, 0, 1),
                maxOutputTokens = Math.Clamp(settings.MaxOutputTokens, 100, 2048),
                responseMimeType = "application/json",
                responseJsonSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        reply = new { type = "string" },
                        suggestedCategory = new { type = new[] { "string", "null" } },
                        severity = new
                        {
                            anyOf = new object[]
                            {
                                new { type = "string", @enum = new[] { "low", "medium", "high", "critical" } },
                                new { type = "null" }
                            }
                        },
                        safetyRisk = new { type = new[] { "boolean", "null" } },
                        needsProfessional = new { type = new[] { "boolean", "null" } },
                        nextStep = new { type = new[] { "string", "null" } },
                        confidence = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                    },
                    required = new[] { "reply" }
                }
            }
        };
    }

    private static DalilAssistantResult ParseProviderResponse(byte[] payload, DalilAssistantRequest request)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return DalilAssistantResult.Unavailable();
        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finishReason)
            && !string.Equals(finishReason.GetString(), "STOP", StringComparison.OrdinalIgnoreCase))
            return DalilAssistantResult.Unavailable();
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var textElement))
            return DalilAssistantResult.Unavailable();
        var raw = textElement.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return DalilAssistantResult.Unavailable();

        var parsed = JsonSerializer.Deserialize<ProviderAnswer>(raw, Json);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Reply) || parsed.Reply.Length > 2000
            || parsed.SuggestedCategory?.Length > 120 || parsed.NextStep?.Length > 1000
            || parsed.Confidence is < 0 or > 1 || !TrySeverity(parsed.Severity, out var severity))
            return DalilAssistantResult.Unavailable();

        var reply = parsed.Reply.Trim();
        var safetyRisk = parsed.SafetyRisk;
        var needsProfessional = parsed.NeedsProfessional;
        var nextStep = parsed.NextStep;
        if (HasHighRisk(request) || safetyRisk == true || severity is DalilSeverity.High or DalilSeverity.Critical)
        {
            reply = "قد يشير الوصف إلى خطر يحتاج تقييمًا من مختص، ولا يمكن تأكيد السبب أو إجراء إصلاح آمن عن بُعد.";
            safetyRisk = true;
            needsProfessional = true;
            severity = severity is DalilSeverity.Critical ? severity : DalilSeverity.High;
            nextStep = "ابتعد عن مصدر الخطر ولا تحاول إصلاحه. تواصل مع مختص مؤهل، ومع جهة الطوارئ المحلية المناسبة عند وجود خطر فوري.";
        }

        return DalilAssistantResult.Success(new DalilAnswer(
            reply, parsed.SuggestedCategory?.Trim(), severity, safetyRisk,
            needsProfessional, nextStep?.Trim(), parsed.Confidence));
    }

    private static bool IsBounded(DalilAssistantRequest request)
    {
        if (request.Messages is not { Count: > 0 and <= 12 }) return false;
        var total = 0;
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 2000) return false;
            total += message.Content.Length;
            if (total > 6000) return false;
        }
        return request.Messages[^1].Role == DalilChatRole.User;
    }

    private static bool TryEndpoint(DalilAssistantOptions settings, out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps) return false;
        var model = settings.Model.Trim();
        if (model.Length > 100 || model.Any(character => !(char.IsLetterOrDigit(character)
            || character is '-' or '_' or '.'))) return false;
        endpoint = new Uri(baseUri, $"models/{Uri.EscapeDataString(model)}:generateContent");
        return true;
    }

    private static bool TrySeverity(string? value, out DalilSeverity? severity)
    {
        severity = value?.ToLowerInvariant() switch
        {
            null => null,
            "low" => DalilSeverity.Low,
            "medium" => DalilSeverity.Medium,
            "high" => DalilSeverity.High,
            "critical" => DalilSeverity.Critical,
            _ => (DalilSeverity?)(-1)
        };
        return severity != (DalilSeverity?)(-1);
    }

    private static bool HasHighRisk(DalilAssistantRequest request)
    {
        string[] terms = ["غاز", "تسرب", "حريق", "دخان", "شرر", "سلك مكشوف", "لوحة كهرباء",
            "انهيار", "صعق", "gas", "fire", "smoke", "spark", "exposed wire", "collapse"];
        return request.Messages.Where(message => message.Role == DalilChatRole.User)
            .Any(message => terms.Any(term => message.Content.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private sealed class ProviderAnswer
    {
        public string? Reply { get; set; }
        public string? SuggestedCategory { get; set; }
        public string? Severity { get; set; }
        public bool? SafetyRisk { get; set; }
        public bool? NeedsProfessional { get; set; }
        public string? NextStep { get; set; }
        public double? Confidence { get; set; }
    }
}
