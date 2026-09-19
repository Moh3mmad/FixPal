using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Services;

public class RequestEvidencePolicy(ApplicationDbContext db, RequestAgreementPolicy agreement)
{
    // Grants must come from RequestAccessService; writes recheck under the request row lock.
    public async Task<EvidenceKind?> UploadKindAsync(RequestAccess? grant, CancellationToken ct)
    {
        if (grant == null) return null;
        var r = grant.Request;
        if (grant.IsOwner && !grant.IsProvider && r.StartedAtUtc == null && r.CompletedAtUtc == null
            && r.Status is MaintenanceRequestStatus.Pending or MaintenanceRequestStatus.Accepted)
            return EvidenceKind.Before;
        if (grant.IsProvider && !grant.IsOwner && r.Status == MaintenanceRequestStatus.InProgress
            && r.StartedAtUtc != null && r.CompletedAtUtc == null
            && await agreement.AgreedRequests.AnyAsync(a => a.Id == r.Id, ct)
            && await ProviderEligibility.ForRequest(db, r.ServiceCategoryId, r.AreaId, r.CustomerId)
                .AnyAsync(p => p.Id == r.ProviderProfileId, ct))
            return EvidenceKind.After;
        return null;
    }
}
