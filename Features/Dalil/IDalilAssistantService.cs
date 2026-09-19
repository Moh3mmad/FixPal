namespace FixPal.Features.Dalil;

public interface IDalilAssistantService
{
    Task<DalilAssistantResult> RespondAsync(DalilAssistantRequest request, CancellationToken cancellationToken);
}
