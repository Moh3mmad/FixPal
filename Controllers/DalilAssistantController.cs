using FixPal.Features.Dalil;
using FixPal.Models.ViewModels.Dalil;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace FixPal.Controllers;

[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DalilAssistantController(
    IDalilAssistantService assistant,
    IDalilSafeContextService safeContext,
    IDalilProviderRecommendationService recommendations,
    ILogger<DalilAssistantController> logger) : Controller
{
    private const int MaximumMessages = 12;
    private const int MaximumMessageLength = 2000;
    private const int MaximumConversationLength = 6000;

    [HttpPost, EnableRateLimiting("diagnosis")]
    public async Task<IActionResult> Send([FromBody] DalilChatRequest? request, CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var messages))
            return BadRequest(DalilChatResponse.Invalid());

        try
        {
            var catalog = await safeContext.GetAsync(cancellationToken);
            var result = await assistant.RespondAsync(
                new DalilAssistantRequest(messages, catalog.ToAssistantContext()), cancellationToken);
            if (result is not { Outcome: DalilOutcome.Success, Answer: not null })
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DalilChatResponse.Unavailable());

            var category = catalog.ResolveCategory(result.Answer.SuggestedCategory);
            var area = catalog.ResolveArea(result.Answer.SuggestedCity, result.Answer.SuggestedArea);
            IReadOnlyList<DalilProviderRecommendation> providers = [];
            if (category != null && area != null)
            {
                providers = await recommendations.FindAsync(
                    category.Id,
                    area.Id,
                    area.CityId,
                    area.CityName,
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    cancellationToken);
            }

            var locationRequired = category != null && area == null;
            return Ok(DalilChatResponse.From(
                result.Answer,
                category?.Name,
                area?.CityName,
                area?.Name,
                locationRequired,
                providers));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Dalil request failed ({FailureType})", exception.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, DalilChatResponse.Unavailable());
        }
    }

    private static bool TryNormalize(DalilChatRequest? request, out IReadOnlyList<DalilChatMessage> messages)
    {
        messages = [];
        if (request?.Messages is not { Count: > 0 and <= MaximumMessages }) return false;

        var normalized = new List<DalilChatMessage>(request.Messages.Count);
        var totalLength = 0;
        foreach (var message in request.Messages)
        {
            var content = message.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > MaximumMessageLength) return false;
            var role = message.Role?.Trim().ToLowerInvariant() switch
            {
                "user" => DalilChatRole.User,
                "assistant" => DalilChatRole.Assistant,
                _ => (DalilChatRole?)(null)
            };
            if (role == null) return false;
            totalLength += content.Length;
            if (totalLength > MaximumConversationLength) return false;
            normalized.Add(new DalilChatMessage(role.Value, content));
        }

        if (normalized[^1].Role != DalilChatRole.User) return false;
        messages = normalized.AsReadOnly();
        return true;
    }
}
