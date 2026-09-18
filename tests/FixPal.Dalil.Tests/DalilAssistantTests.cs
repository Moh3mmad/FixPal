using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FixPal.Controllers;
using FixPal.Features.Dalil;
using FixPal.Models.ViewModels.Dalil;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FixPal.Dalil.Tests;

public sealed class DalilAssistantTests
{
    [Fact]
    public async Task ValidMessageReturnsStructuredSuccessfulResponse()
    {
        var expected = new DalilAnswer(
            "يبدو أن هناك تسربًا بسيطًا.",
            SuggestedCategory: "سباكة",
            Severity: DalilSeverity.Medium,
            SafetyRisk: false,
            NeedsProfessional: true,
            NextStep: "أغلق مصدر الماء إن كان ذلك آمنًا وتواصل مع سباك.",
            Confidence: 0.72);
        var controller = Controller(new StubAssistant(DalilAssistantResult.Success(expected)));

        var result = await controller.Send(Request("المغسلة تسرّب الماء"), default);

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<DalilChatResponse>(response.Value);
        Assert.True(body.Success);
        Assert.Equal(expected.Reply, body.Reply);
        Assert.Equal(expected.Reply, body.HistoryReply);
        Assert.Equal("سباكة", body.SuggestedCategory);
        Assert.Equal(DalilSeverity.Medium, body.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyMessageReturnsValidationFailure(string message)
    {
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer("unused")));
        var result = await Controller(assistant).Send(Request(message), default);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(Assert.IsType<DalilChatResponse>(response.Value).Success);
        Assert.Equal(0, assistant.CallCount);
    }

    [Fact]
    public async Task OversizedMessageReturnsValidationFailureWithoutCallingService()
    {
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer("unused")));

        var result = await Controller(assistant).Send(Request(new string('x', 2001)), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, assistant.CallCount);
    }

    [Fact]
    public async Task MissingCredentialFailsGracefullyWithoutCallingProvider()
    {
        var handler = new DelegateHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        var service = Service(handler, new DalilAssistantOptions { GeminiApiKey = null });

        var result = await service.RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ProviderTimeoutFailsGracefully()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = Service(handler, Settings(timeoutSeconds: 1));

        var result = await service.RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(handler, Settings()).RespondAsync(ServiceRequest(), cancellation.Token));
    }

    [Fact]
    public async Task ProviderApiErrorFailsGracefully()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("secret provider details")
            }));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Transient503ThenSuccessIsRetriedAndParsed()
    {
        var handler = SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            GeminiResponse("{\"reply\":\"نجحت المحاولة الثانية.\"}"));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("نجحت المحاولة الثانية.", result.Answer!.Reply);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task TwoTransientFailuresThenSuccessUsesThreeTotalAttempts()
    {
        var handler = SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            GeminiResponse("{\"reply\":\"نجحت المحاولة الثالثة.\"}"));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("نجحت المحاولة الثالثة.", result.Answer!.Reply);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task TransientRetriesStopAfterThreeTotalAttempts()
    {
        var logs = new RecordingLogger<GeminiDalilAssistantService>();
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.GatewayTimeout)));

        var result = await Service(handler, Settings(), logs).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        Assert.Equal(3, handler.CallCount);
        Assert.Contains(logs.Messages, message =>
            message.Contains("transient failure exhausted after 3 attempts", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PermanentClientErrorsAreNotRetried(HttpStatusCode statusCode)
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CallerCancellationStopsTransientRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new DelegateHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(handler, Settings()).RespondAsync(ServiceRequest(), cancellation.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task MalformedProviderResponseFailsGracefully()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(JsonResponse("{not-json")));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task GeminiRequestIsServerSideStructuredAndKeepsKeyOutOfUrlAndBody()
    {
        const string key = "test-only-secret-key";
        HttpRequestMessage? captured = null;
        string? requestBody = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            captured = request;
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var answer = """{"reply":"افحص مصدر الصوت دون فتح الجهاز.","suggestedCategory":"أجهزة","suggestedCity":"رام الله","suggestedArea":"الطيرة","severity":"low","safetyRisk":false,"needsProfessional":true,"nextStep":"تواصل مع فني أجهزة.","confidence":0.6}""";
            return GeminiResponse(answer);
        });
        var settings = Settings();
        settings.GeminiApiKey = key;
        settings.Model = "gemini-3.5-flash-lite";

        var result = await Service(handler, settings).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("أجهزة", result.Answer!.SuggestedCategory);
        Assert.Equal("رام الله", result.Answer.SuggestedCity);
        Assert.Equal("الطيرة", result.Answer.SuggestedArea);
        Assert.NotNull(captured);
        Assert.Equal(key, captured!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(key, captured.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, requestBody!, StringComparison.Ordinal);
        using var providerRequest = JsonDocument.Parse(requestBody!);
        var generationConfig = providerRequest.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal("object", generationConfig.GetProperty("responseJsonSchema").GetProperty("type").GetString());
        var schemaProperties = generationConfig.GetProperty("responseJsonSchema").GetProperty("properties");
        Assert.True(schemaProperties.TryGetProperty("suggestedCity", out _));
        Assert.True(schemaProperties.TryGetProperty("suggestedArea", out _));
        Assert.False(generationConfig.TryGetProperty("responseFormat", out _));
        Assert.Equal(1024, generationConfig.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("minimal", generationConfig.GetProperty("thinkingConfig")
            .GetProperty("thinkingLevel").GetString());
        Assert.False(generationConfig.TryGetProperty("temperature", out _));
        var systemPrompt = providerRequest.RootElement.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal(DalilSystemPrompt.Text, systemPrompt);
    }

    [Fact]
    public async Task Gemini25RequestOmitsThinkingLevelAndKeepsTemperature()
    {
        string? requestBody = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return GeminiResponse("{\"reply\":\"رد صالح.\"}");
        });
        var settings = Settings();
        settings.Model = "gemini-2.5-flash";

        var result = await Service(handler, settings).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        using var providerRequest = JsonDocument.Parse(requestBody!);
        var generationConfig = providerRequest.RootElement.GetProperty("generationConfig");
        Assert.False(generationConfig.TryGetProperty("thinkingConfig", out _));
        Assert.Equal(0.2, generationConfig.GetProperty("temperature").GetDouble(), 10);
    }

    [Fact]
    public async Task Gemini35FlashLiteUsesMinimalThinkingAndConciseDefaultBudget()
    {
        string? requestBody = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return GeminiResponse("{\"reply\":\"رد سريع.\"}");
        });
        var settings = new DalilAssistantOptions { GeminiApiKey = "test-key" };

        var result = await Service(handler, settings).RespondAsync(ServiceRequest(), default);

        Assert.Equal("gemini-3.5-flash-lite", settings.Model);
        Assert.Equal(1024, settings.MaxOutputTokens);
        Assert.Equal(DalilOutcome.Success, result.Outcome);
        using var providerRequest = JsonDocument.Parse(requestBody!);
        var generationConfig = providerRequest.RootElement.GetProperty("generationConfig");
        var thinkingConfig = generationConfig.GetProperty("thinkingConfig");
        Assert.Equal("minimal", thinkingConfig.GetProperty("thinkingLevel").GetString());
        Assert.False(thinkingConfig.TryGetProperty("thinkingBudget", out _));
        Assert.False(generationConfig.TryGetProperty("temperature", out _));
        Assert.Equal(1024, generationConfig.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task GeminiReceivesOnlyCategoryAndLocationCatalogData()
    {
        string? requestBody = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return GeminiResponse("{\"reply\":\"رد صالح.\"}");
        });
        var safeContext = new DalilSafeContext(
            [new DalilPublicServiceCategory("سباكة", "صيانة المياه")],
            [new DalilPublicLocation("رام الله", "الطيرة")]);
        var request = new DalilAssistantRequest(
            [new DalilChatMessage(DalilChatRole.User, "تسريب")], safeContext);

        var result = await Service(handler, new DalilAssistantOptions { GeminiApiKey = "test-key" })
            .RespondAsync(request, default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        using var providerRequest = JsonDocument.Parse(requestBody!);
        var contextText = providerRequest.RootElement.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString()!;
        var contextJson = contextText[(contextText.IndexOf('\n') + 1)..];
        using var contextDocument = JsonDocument.Parse(contextJson);
        var properties = contextDocument.RootElement.EnumerateObject()
            .Select(property => property.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "serviceCategories", "locations" }, properties);
        Assert.Equal("سباكة", contextDocument.RootElement.GetProperty("serviceCategories")[0]
            .GetProperty("name").GetString());
        var location = contextDocument.RootElement.GetProperty("locations")[0];
        Assert.Equal("رام الله", location.GetProperty("city").GetString());
        Assert.Equal("الطيرة", location.GetProperty("area").GetString());
        foreach (var forbidden in SensitiveNames)
            Assert.DoesNotContain(forbidden, contextJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaximumTokensFinishReasonFailsSafelyEvenWithUsableText()
    {
        var logs = new RecordingLogger<GeminiDalilAssistantService>();
        var handler = new DelegateHandler((_, _) => Task.FromResult(GeminiResponseWithFinishReason(
            "MAX_TOKENS",
            new { text = "{\"reply\":\"truncated but valid-looking\"}" })));

        var result = await Service(handler, Settings(), logs).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        Assert.Contains(logs.Messages, message =>
            message.Contains("finish_reason", StringComparison.Ordinal)
            && message.Contains("FinishReason=MAX_TOKENS", StringComparison.Ordinal)
            && message.Contains("UsableTextFound=True", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gemini35ResponseUsesLaterTextAndIgnoresThoughtAndSignatureOnlyParts()
    {
        var answer = """{"reply":"الجزء النهائي صالح.","suggestedCategory":null,"severity":null,"safetyRisk":false,"needsProfessional":null,"nextStep":null,"confidence":null}""";
        var handler = new DelegateHandler((_, _) => Task.FromResult(GeminiResponse(
            new { text = "ملخص تفكير لا يجب قراءته", thought = true },
            new { thoughtSignature = "opaque-test-signature" },
            new { text = answer, thoughtSignature = "opaque-final-signature" })));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("الجزء النهائي صالح.", result.Answer!.Reply);
    }

    [Fact]
    public async Task Gemini35ResponseCombinesStructuredJsonAcrossNonThoughtTextParts()
    {
        const string first = "{\"reply\":\"رد من";
        const string second = " عدة أجزاء\",\"severity\":\"low\"}";
        var handler = new DelegateHandler((_, _) => Task.FromResult(GeminiResponse(
            new { text = first },
            new { thoughtSignature = "signature-only" },
            new { text = second })));

        var result = await Service(handler, Settings()).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("رد من عدة أجزاء", result.Answer!.Reply);
        Assert.Equal(DalilSeverity.Low, result.Answer.Severity);
    }

    [Fact]
    public async Task MalformedStructuredTextFallsBackAndLogsOnlySafeMetadata()
    {
        var logs = new RecordingLogger<GeminiDalilAssistantService>();
        var handler = new DelegateHandler((_, _) => Task.FromResult(GeminiResponse(
            new { text = "not valid structured JSON" })));

        var result = await Service(handler, Settings(), logs).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Unavailable, result.Outcome);
        var log = Assert.Single(logs.Messages);
        Assert.Contains("malformed_structured_json", log, StringComparison.Ordinal);
        Assert.Contains("CandidateCount=1", log, StringComparison.Ordinal);
        Assert.Contains("ContentPartCount=1", log, StringComparison.Ordinal);
        Assert.DoesNotContain("not valid structured JSON", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControllerNeverReturnsSecretFromUnexpectedProviderException()
    {
        const string secret = "should-never-reach-browser";
        var result = await Controller(new ThrowingAssistant(secret)).Send(Request("رسالة صالحة"), default);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, response.StatusCode);
        var body = Assert.IsType<DalilChatResponse>(response.Value);
        var serialized = JsonSerializer.Serialize(response.Value);
        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.Equal(DalilChatResponse.UnavailableMessage, body.Reply);
    }

    [Fact]
    public void StructuredContractSupportsOptionalFieldsAndSafeContext()
    {
        var context = new DalilSafeContext(
            [new DalilPublicServiceCategory("كهرباء", "صيانة كهربائية")],
            [new DalilPublicLocation("رام الله", "الطيرة")]);
        var request = new DalilAssistantRequest([new(DalilChatRole.User, "المشكلة")], context);
        var answer = new DalilAnswer("رد", Confidence: null);

        Assert.Equal("كهرباء", request.Context!.ServiceCategories!.Single().Name);
        Assert.Null(answer.Confidence);
        Assert.Null(answer.Severity);
    }

    [Fact]
    public async Task ValidCategoryAndAreaUseBackendMatchingOnceAndReturnWhitelistedProvider()
    {
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "وجدت التخصص والموقع.",
            SuggestedCategory: "سباكة",
            SuggestedCity: "رام الله",
            SuggestedArea: "الطيرة")));
        var context = new StubSafeContext(DefaultCatalog());
        var provider = new DalilProviderRecommendation(
            17, "مزود حقيقي", "سباكة", "رام الله", "الطيرة", 4.7, 12,
            "متاح الآن", DalilProviderMatchScope.ExactArea,
            "/Providers/Details/17",
            "/MaintenanceRequests/Create?categoryId=2&providerProfileId=17");
        var matching = new StubRecommendations([provider]);

        var action = await Controller(assistant, context, matching)
            .Send(Request("عندي تسريب في الطيرة، رام الله"), default);

        var response = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<DalilChatResponse>(response.Value);
        Assert.Equal(1, assistant.CallCount);
        Assert.Equal(1, matching.CallCount);
        Assert.Equal(2, matching.CategoryId);
        Assert.Equal(8, matching.AreaId);
        Assert.False(body.LocationRequired);
        Assert.Equal(provider, Assert.Single(body.Providers!));
        Assert.Contains("مزود حقيقي — سباكة — 4.7/5", body.Reply, StringComparison.Ordinal);
        Assert.Equal("وجدت التخصص والموقع.", body.HistoryReply);
        Assert.DoesNotContain("مزود حقيقي", body.HistoryReply, StringComparison.Ordinal);
        Assert.DoesNotContain("مزود حقيقي", assistant.LastRequest!.Messages.Single().Content, StringComparison.Ordinal);

        var contextJson = JsonSerializer.Serialize(assistant.LastRequest!.Context);
        Assert.DoesNotContain("مزود حقيقي", contextJson, StringComparison.Ordinal);
        foreach (var forbidden in SensitiveNames)
            Assert.DoesNotContain(forbidden, contextJson, StringComparison.OrdinalIgnoreCase);

        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var providerJson = responseJson.RootElement.GetProperty("Providers")[0];
        var names = providerJson.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Equal(new HashSet<string>
        {
            "Id", "DisplayName", "Specialty", "City", "Area", "AverageRating",
            "ReviewCount", "AvailabilityLabel", "MatchScope", "ProfileUrl", "RequestUrl"
        }, names);
    }

    [Fact]
    public async Task MultiTurnHistoryUsesCleanGeminiReplyAndNeverRetransmitsProviderData()
    {
        const string cleanReply = "وجدت التخصص والموقع وسأعرض الخيارات المتاحة.";
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            cleanReply,
            SuggestedCategory: "سباكة",
            SuggestedCity: "رام الله",
            SuggestedArea: "الطيرة")));
        var provider = new DalilProviderRecommendation(
            9173, "مزود حقيقي", "سباكة", "رام الله", "منطقة حقيقية", 4.7, 12,
            "متاح الآن", DalilProviderMatchScope.SameCity,
            "/Providers/Details/9173",
            "/MaintenanceRequests/Create?categoryId=2&cityId=3&areaId=8");
        var controller = Controller(
            assistant,
            new StubSafeContext(DefaultCatalog()),
            new StubRecommendations([provider]));

        var firstAction = await controller.Send(Request("عندي تسريب في الطيرة"), default);
        var first = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(firstAction).Value);

        Assert.Contains("مزود حقيقي", first.Reply, StringComparison.Ordinal);
        Assert.Contains("منطقة حقيقية", first.Reply, StringComparison.Ordinal);
        Assert.Contains("4.7/5", first.Reply, StringComparison.Ordinal);
        Assert.Equal(cleanReply, first.HistoryReply);
        string[] providerValues =
        [
            "مزود حقيقي", "منطقة حقيقية", "4.7", "9173",
            "/Providers/Details/9173", "/MaintenanceRequests/Create"
        ];
        foreach (var value in providerValues)
            Assert.DoesNotContain(value, first.HistoryReply, StringComparison.Ordinal);

        var secondRequest = new DalilChatRequest
        {
            Messages =
            [
                new DalilChatMessageInput { Role = "user", Content = "عندي تسريب في الطيرة" },
                new DalilChatMessageInput { Role = "assistant", Content = first.HistoryReply },
                new DalilChatMessageInput { Role = "user", Content = "هل يوجد خيار مناسب؟" }
            ]
        };
        await controller.Send(secondRequest, default);

        Assert.Equal(2, assistant.CallCount);
        var secondGeminiRequest = string.Join(
            "\n",
            assistant.LastRequest!.Messages.Select(message => message.Content));
        foreach (var value in providerValues)
            Assert.DoesNotContain(value, secondGeminiRequest, StringComparison.Ordinal);
        Assert.Contains(cleanReply, secondGeminiRequest, StringComparison.Ordinal);
        Assert.DoesNotContain(
            assistant.LastRequest.Context!.Locations!,
            location => location.Area == "منطقة حقيقية");
    }

    [Fact]
    public async Task TextSummaryUsesAtMostThreeBackendProvidersWhileCardsKeepAllMatches()
    {
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "نتيجة المطابقة جاهزة.",
            SuggestedCategory: "سباكة",
            SuggestedCity: "رام الله",
            SuggestedArea: "الطيرة")));
        var providers = Enumerable.Range(1, 4).Select(index => new DalilProviderRecommendation(
            index,
            $"مزود {index}",
            "سباكة",
            "رام الله",
            "الطيرة",
            index == 2 ? null : 4.0 + index / 10.0,
            index,
            "متاح الآن",
            DalilProviderMatchScope.ExactArea,
            $"/Providers/Details/{index}",
            $"/MaintenanceRequests/Create?categoryId=2&providerProfileId={index}")).ToArray();

        var action = await Controller(
                assistant,
                new StubSafeContext(DefaultCatalog()),
                new StubRecommendations(providers))
            .Send(Request("تسريب في الطيرة"), default);

        var body = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.Contains("مزود 1 — سباكة — 4.1/5", body.Reply, StringComparison.Ordinal);
        Assert.Contains("مزود 2 — سباكة", body.Reply, StringComparison.Ordinal);
        Assert.Contains("مزود 3 — سباكة — 4.3/5", body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain("مزود 4", body.Reply, StringComparison.Ordinal);
        Assert.Equal(4, body.Providers!.Count);
        Assert.Equal(1, assistant.CallCount);
    }

    [Fact]
    public async Task ExactAreaMatchesTakePrecedenceAndProduceProviderSelectedRequestUrl()
    {
        var exact = ProviderMatch(17, "مزود المنطقة", "الطيرة", "رام الله", 4.8, 9);
        var matching = new StubProviderMatchingService([exact],
            [ProviderMatch(18, "مزود احتياطي", "الماصيون", "رام الله", 4.6, 5)]);
        var service = new DalilProviderRecommendationService(matching);

        var results = await service.FindAsync(2, 8, 3, "رام الله", "customer-1", default);

        var result = Assert.Single(results);
        Assert.Equal(DalilProviderMatchScope.ExactArea, result.MatchScope);
        Assert.Equal(1, matching.ExactCalls);
        Assert.Equal(0, matching.CityCalls);
        Assert.Equal("/Providers/Details/17", result.ProfileUrl);
        Assert.Contains("providerProfileId=17", result.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("categoryId=2", result.RequestUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("cityId", result.RequestUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("areaId", result.RequestUrl, StringComparison.Ordinal);
        Assert.Equal(4.8, result.AverageRating);
        Assert.Equal(9, result.ReviewCount);
    }

    [Fact]
    public async Task EmptyExactAreaFallsBackToSameCityAndNeverReturnsAnotherCity()
    {
        var sameCity = ProviderMatch(18, "مزود من الماصيون", "الماصيون", "رام الله", 4.6, 5);
        var otherCity = ProviderMatch(19, "مزود من نابلس", "رفيديا", "نابلس", 4.9, 20);
        var matching = new StubProviderMatchingService([], [sameCity, otherCity]);
        var service = new DalilProviderRecommendationService(matching);

        var results = await service.FindAsync(2, 8, 3, "رام الله", "customer-1", default);

        var result = Assert.Single(results);
        Assert.Equal(18, result.Id);
        Assert.Equal(DalilProviderMatchScope.SameCity, result.MatchScope);
        Assert.Equal(1, matching.ExactCalls);
        Assert.Equal(1, matching.CityCalls);
        Assert.Equal(3, matching.CityId);
        Assert.Equal(8, matching.ExcludedAreaId);
        Assert.Equal("/Providers/Details/18", result.ProfileUrl);
        Assert.DoesNotContain("providerProfileId", result.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("categoryId=2", result.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("cityId=3", result.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("areaId=8", result.RequestUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameCityTextNamesActualAreaAndKeepsFallbackProviderUnassigned()
    {
        var provider = new DalilProviderRecommendation(
            18, "مزود قريب", "سباكة", "رام الله", "الماصيون", null, 0,
            "متاح الآن", DalilProviderMatchScope.SameCity,
            "/Providers/Details/18",
            "/MaintenanceRequests/Create?categoryId=2&cityId=3&areaId=8");
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "سأعرض الخيارات المتاحة.", SuggestedCategory: "سباكة",
            SuggestedCity: "رام الله", SuggestedArea: "الطيرة")));

        var action = await Controller(
                assistant,
                new StubSafeContext(DefaultCatalog()),
                new StubRecommendations([provider]))
            .Send(Request("تسريب في الطيرة"), default);

        var body = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.Contains("من نفس المدينة", body.Reply, StringComparison.Ordinal);
        Assert.Contains("مزود قريب — سباكة — الماصيون", body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain("/5", body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain("providerProfileId", body.Providers!.Single().RequestUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAreaRequiresLocationAndDoesNotMatchProviders()
    {
        var matching = new StubRecommendations([]);
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "ما منطقتك؟", SuggestedCategory: "سباكة", SuggestedCity: "رام الله")));

        var action = await Controller(assistant, new StubSafeContext(DefaultCatalog()), matching)
            .Send(Request("عندي تسريب"), default);

        var body = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.True(body.LocationRequired);
        Assert.Empty(body.Providers!);
        Assert.Equal(0, matching.CallCount);
        Assert.Equal("ما منطقتك؟", body.Reply);
    }

    [Theory]
    [InlineData("تخصص مخترع", "رام الله", "الطيرة")]
    [InlineData("سباكة", "رام الله", "منطقة مخترعة")]
    [InlineData("سباكة", "مدينة مخترعة", "الطيرة")]
    public async Task InventedCategoryOrAreaNeverTriggersProviderMatching(
        string category,
        string city,
        string area)
    {
        var matching = new StubRecommendations([]);
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "أحتاج توضيحًا.", SuggestedCategory: category, SuggestedCity: city, SuggestedArea: area)));

        var action = await Controller(assistant, new StubSafeContext(DefaultCatalog()), matching)
            .Send(Request("مشكلة صيانة"), default);

        var body = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.Equal(0, matching.CallCount);
        Assert.Empty(body.Providers!);
        if (category == "تخصص مخترع") Assert.Null(body.SuggestedCategory);
        else Assert.Null(body.SuggestedArea);
    }

    [Fact]
    public async Task NoEligibleProviderReturnsEmptyListWithoutSecondGeminiCall()
    {
        var assistant = new StubAssistant(DalilAssistantResult.Success(new DalilAnswer(
            "لا توجد نتيجة حاليًا.",
            SuggestedCategory: "سباكة",
            SuggestedCity: "رام الله",
            SuggestedArea: "الطيرة")));
        var matching = new StubRecommendations([]);

        var action = await Controller(assistant, new StubSafeContext(DefaultCatalog()), matching)
            .Send(Request("تسريب في الطيرة"), default);

        var body = Assert.IsType<DalilChatResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.Empty(body.Providers!);
        Assert.Equal("لا توجد نتيجة حاليًا.", body.Reply);
        Assert.Equal(1, assistant.CallCount);
        Assert.Equal(1, matching.CallCount);
    }

    [Fact]
    public void ControllerRequiresAuthentication()
    {
        Assert.NotNull(typeof(DalilAssistantController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
    }

    [Fact]
    public void BrowserAssetsAreIsolatedSafeAndResponsive()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "dalil-assistant.js"));
        var view = File.ReadAllText(Path.Combine(root, "Views", "Shared", "Dalil", "_DalilAssistantWidget.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "components", "dalil-assistant.css"));

        Assert.DoesNotContain("generativelanguage.googleapis.com", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GeminiApiKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x-goog-api-key", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.Contains("دليل — المساعد الذكي في منصة FixPal", view, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 575.98px)", css, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh", css, StringComparison.Ordinal);
        Assert.Contains("[data-dalil-widget]", script, StringComparison.Ordinal);
        Assert.Contains("عرض الملف الشخصي", script, StringComparison.Ordinal);
        Assert.Contains("تقديم طلب صيانة", script, StringComparison.Ordinal);
        Assert.Contains("provider.reviewCount", script, StringComparison.Ordinal);
        Assert.Contains("toFixed(1)", script, StringComparison.Ordinal);
        Assert.Contains("لا توجد تقييمات بعد", script, StringComparison.Ordinal);
        Assert.Contains("addMessage('assistant', payload.reply, payload)", script, StringComparison.Ordinal);
        Assert.Contains("content: payload.historyReply.trim()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("content: payload.reply", script, StringComparison.Ordinal);
        Assert.DoesNotContain("payload.historyReply || payload.reply", script, StringComparison.Ordinal);
        Assert.DoesNotContain("payload.historyReply ?? payload.reply", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }

    private static FixPal.Services.ProviderMatch ProviderMatch(
        int id,
        string name,
        string area,
        string city,
        double? rating,
        int reviews) => new(id, name, "سباكة", city, area, "متاح الآن", rating, reviews);

    private static readonly string[] SensitiveNames =
    [
        "Email", "PhoneNumber", "UserId", "PasswordHash", "SecurityStamp",
        "MaintenanceRequest", "Appointment"
    ];

    private static DalilAssistantController Controller(
        IDalilAssistantService assistant,
        IDalilSafeContextService? context = null,
        IDalilProviderRecommendationService? recommendations = null)
    {
        var controller = new DalilAssistantController(
            assistant,
            context ?? new StubSafeContext(DefaultCatalog()),
            recommendations ?? new StubRecommendations([]),
            NullLogger<DalilAssistantController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "customer-1")], "test"))
            }
        };
        return controller;
    }

    private static DalilSafeCatalog DefaultCatalog() => new(
        [new DalilCategoryOption(2, "سباكة", "صيانة شبكات المياه")],
        [new DalilAreaOption(8, "الطيرة", 3, "رام الله")]);

    private static DalilChatRequest Request(string message) => new()
    {
        Messages = [new DalilChatMessageInput { Role = "user", Content = message }]
    };

    private static DalilAssistantRequest ServiceRequest() =>
        new([new(DalilChatRole.User, "الثلاجة تصدر صوتًا غريبًا")]);

    private static DalilAssistantOptions Settings(int timeoutSeconds = 5) => new()
    {
        GeminiApiKey = "test-key",
        Model = "gemini-test",
        Endpoint = "https://generativelanguage.googleapis.com/v1beta/",
        TimeoutSeconds = timeoutSeconds
    };

    private static GeminiDalilAssistantService Service(
        HttpMessageHandler handler,
        DalilAssistantOptions settings,
        ILogger<GeminiDalilAssistantService>? logger = null) =>
        new(new HttpClient(handler), Options.Create(settings),
            logger ?? NullLogger<GeminiDalilAssistantService>.Instance);

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage GeminiResponse(string answer) => GeminiResponse(new { text = answer });

    private static HttpResponseMessage GeminiResponse(params object[] parts) =>
        GeminiResponseWithFinishReason("STOP", parts);

    private static HttpResponseMessage GeminiResponseWithFinishReason(
        string finishReason,
        params object[] parts) => JsonResponse(JsonSerializer.Serialize(new
    {
        candidates = new[] { new { finishReason, content = new { parts } } }
    }));

    private static DelegateHandler SequenceHandler(params HttpResponseMessage[] responses)
    {
        var next = 0;
        return new DelegateHandler((_, _) => Task.FromResult(responses[next++]));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FixPal.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class StubAssistant(DalilAssistantResult result) : IDalilAssistantService
    {
        public int CallCount { get; private set; }
        public DalilAssistantRequest? LastRequest { get; private set; }
        public Task<DalilAssistantResult> RespondAsync(DalilAssistantRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubSafeContext(DalilSafeCatalog catalog) : IDalilSafeContextService
    {
        public Task<DalilSafeCatalog> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(catalog);
    }

    private sealed class StubRecommendations(IReadOnlyList<DalilProviderRecommendation> results)
        : IDalilProviderRecommendationService
    {
        public int CallCount { get; private set; }
        public int? CategoryId { get; private set; }
        public int? AreaId { get; private set; }

        public Task<IReadOnlyList<DalilProviderRecommendation>> FindAsync(
            int categoryId,
            int areaId,
            int cityId,
            string cityName,
            string? ownerId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CategoryId = categoryId;
            AreaId = areaId;
            return Task.FromResult(results);
        }
    }

    private sealed class StubProviderMatchingService(
        IReadOnlyList<FixPal.Services.ProviderMatch> exact,
        IReadOnlyList<FixPal.Services.ProviderMatch> sameCity)
        : FixPal.Services.ProviderMatchingService(null!)
    {
        public int ExactCalls { get; private set; }
        public int CityCalls { get; private set; }
        public int? CityId { get; private set; }
        public int? ExcludedAreaId { get; private set; }

        public override Task<IReadOnlyList<FixPal.Services.ProviderMatch>> FindAsync(
            int categoryId,
            int areaId,
            string? name,
            CancellationToken ct,
            string? ownerId = null)
        {
            ExactCalls++;
            return Task.FromResult(exact);
        }

        public override Task<IReadOnlyList<FixPal.Services.ProviderMatch>> FindInCityAsync(
            int categoryId,
            int cityId,
            int excludedAreaId,
            CancellationToken ct,
            string? ownerId = null)
        {
            CityCalls++;
            CityId = cityId;
            ExcludedAreaId = excludedAreaId;
            return Task.FromResult(sameCity);
        }
    }

    private sealed class ThrowingAssistant(string message) : IDalilAssistantService
    {
        public Task<DalilAssistantResult> RespondAsync(DalilAssistantRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return response(request, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
