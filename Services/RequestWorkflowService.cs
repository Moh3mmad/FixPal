using System.Security.Claims;
using FixPal.Data;
using FixPal.Infrastructure;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;

public class RequestWorkflowService(ApplicationDbContext db, RequestAccessService access,
    RequestMutationService mutations, RequestAgreementPolicy agreement)
{
    public IQueryable<MaintenanceRequest> Claimable(int providerId, string userId)
    {
        var eligibleProviders = ProviderEligibility.Active(db).Where(p => p.Id == providerId && p.UserId == userId);
        return db.MaintenanceRequests.Where(r =>
            r.RequestType == RequestType.PrivateService && r.Status == MaintenanceRequestStatus.Pending && r.ProviderProfileId == null
            && r.CustomerId != userId && eligibleProviders.Any(p => p.ServiceCategoryId == r.ServiceCategoryId
                && p.AreaId == r.AreaId && p.UserId != r.CustomerId));
    }

    public Task<MutationResult> ClaimAsync(ClaimsPrincipal user, int id, CancellationToken ct) => mutations.RunAsync(id, async () =>
    {
        var providerId = await access.ProviderIdAsync(user, ct);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (providerId == null || userId == null) return MutationResult.NotFound;
        // Claim also records provider willingness, in one atomic update.
        var changed = await Claimable(providerId.Value, userId).Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.ProviderProfileId, providerId).SetProperty(r => r.Status, MaintenanceRequestStatus.Accepted)
            .SetProperty(r => r.AcceptedAtUtc, DateTime.UtcNow), ct);
        return changed == 1 ? MutationResult.Success : MutationResult.Conflict;
    }, ct);

    public Task<MutationResult> TransitionAsync(ClaimsPrincipal user, int id, MaintenanceRequestStatus from, MaintenanceRequestStatus to, CancellationToken ct) =>
        mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(user, id, ct);
            if (grant is not { IsProvider: true, IsOwner: false }) return MutationResult.NotFound;
            if (!RequestWorkflow.CanTransition(from, to) || grant.Request.Status != from || grant.Request.RequestType != RequestType.PrivateService
                || !await ProviderEligibility.ForRequest(db, grant.Request.ServiceCategoryId, grant.Request.AreaId, grant.Request.CustomerId)
                    .AnyAsync(p => p.Id == grant.Request.ProviderProfileId, ct)) return MutationResult.Conflict;
            if (to is MaintenanceRequestStatus.InProgress or MaintenanceRequestStatus.Completed
                && !await agreement.AgreedRequests.AnyAsync(r => r.Id == id, ct)) return MutationResult.Conflict;
            if (to == MaintenanceRequestStatus.Completed && !await db.RequestQuotes.AnyAsync(q => q.MaintenanceRequestId == id
                && q.FinalPrice != null && q.FinalPriceAcceptedAtUtc != null, ct)) return MutationResult.Conflict;
            var query = db.MaintenanceRequests.Where(r => r.Id == id && r.Status == from && r.ProviderProfileId == grant.Request.ProviderProfileId);
            var now = DateTime.UtcNow;
            var changed = to switch
            {
                MaintenanceRequestStatus.Accepted => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.AcceptedAtUtc, now), ct),
                MaintenanceRequestStatus.InProgress => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.StartedAtUtc, now), ct),
                MaintenanceRequestStatus.Completed => await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.CompletedAtUtc, now), ct),
                _ => 0
            };
            return changed == 1 ? MutationResult.Success : MutationResult.Conflict;
        }, ct);
}
