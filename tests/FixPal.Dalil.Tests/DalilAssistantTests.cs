using System.Net;
using System.Text;
using System.Text.Json;
using FixPal.Controllers;
using FixPal.Features.Dalil;
using FixPal.Models.ViewModels.Dalil;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FixPal.Dalil.Tests;

public sealed class DalilAssistantTests
{
    [Fact]
    public async Task ValidMessageReturnsStructuredSuccessfulResponse()
    {
        var expected = new DalilAnswer("يبدو أن هناك تسربًا بسيطًا.", "سباكة", DalilSeverity.Medium,
            false, true, "أغلق مصدر الماء إن كان ذلك آمنًا وتواصل مع سباك.", 0.72);
        var controller = Controller(new StubAssistant(DalilAssistantResult.Success(expected)));

        var result = await controller.Send(Request("المغسلة تسرّب الماء"), default);

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<DalilChatResponse>(response.Value);
        Assert.True(body.Success);
        Assert.Equal(expected.Reply, body.Reply);
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
            var answer = """{"reply":"افحص مصدر الصوت دون فتح الجهاز.","suggestedCategory":"أجهزة","severity":"low","safetyRisk":false,"needsProfessional":true,"nextStep":"تواصل مع فني أجهزة.","confidence":0.6}""";
            return GeminiResponse(answer);
        });
        var settings = Settings();
        settings.GeminiApiKey = key;

        var result = await Service(handler, settings).RespondAsync(ServiceRequest(), default);

        Assert.Equal(DalilOutcome.Success, result.Outcome);
        Assert.Equal("أجهزة", result.Answer!.SuggestedCategory);
        Assert.NotNull(captured);
        Assert.Equal(key, captured!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(key, captured.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, requestBody!, StringComparison.Ordinal);
        Assert.Contains("responseJsonSchema", requestBody!, StringComparison.Ordinal);
        using var providerRequest = JsonDocument.Parse(requestBody!);
        var systemPrompt = providerRequest.RootElement.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal(DalilSystemPrompt.Text, systemPrompt);
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
            [new DalilPublicServiceCategory("كهرباء")],
            [new DalilPublicLocation("رام الله", "الطيرة")],
            [new DalilPublicProviderSummary("مزود عام", "كهرباء", "الطيرة", "متاح لاحقًا")],
            ["محتوى مساعدة عام"]);
        var request = new DalilAssistantRequest([new(DalilChatRole.User, "المشكلة")], context);
        var answer = new DalilAnswer("رد", Confidence: null);

        Assert.Equal("كهرباء", request.Context!.ServiceCategories!.Single().Name);
        Assert.Null(answer.Confidence);
        Assert.Null(answer.Severity);
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
    }

    private static DalilAssistantController Controller(IDalilAssistantService assistant) =>
        new(assistant, NullLogger<DalilAssistantController>.Instance);

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

    private static GeminiDalilAssistantService Service(HttpMessageHandler handler, DalilAssistantOptions settings) =>
        new(new HttpClient(handler), Options.Create(settings), NullLogger<GeminiDalilAssistantService>.Instance);

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage GeminiResponse(string answer) => JsonResponse(JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new[] { new { text = answer } } } } }
    }));

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
        public Task<DalilAssistantResult> RespondAsync(DalilAssistantRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
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
}
