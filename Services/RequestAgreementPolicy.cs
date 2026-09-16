using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;

public class RequestAgreementPolicy(ApplicationDbContext db)
{
    // Current provider eligibility is checked separately at mutation boundaries.
    // Historical completed agreements remain identifiable after provider role revocation.
    public IQueryable<MaintenanceRequest> AgreedRequests => db.MaintenanceRequests.Where(r =>
        r.RequestType == RequestType.PrivateService && r.AcceptedAtUtc != null && r.ProviderProfile != null
        && r.CustomerId != r.ProviderProfile.UserId && r.Status != MaintenanceRequestStatus.Pending
        && db.RequestQuotes.Any(q => q.MaintenanceRequestId == r.Id && q.ProviderProfileId == r.ProviderProfileId
            && q.State == QuoteState.Accepted && q.AcceptedRevisionNumber != null && q.AcceptedAtUtc != null
            && db.QuoteDecisions.Any(d => d.RequestQuoteId == q.Id && d.RevisionNumber == q.AcceptedRevisionNumber
                && d.State == QuoteState.Accepted && d.CustomerAuthorId == r.CustomerId)));

    public Task<bool> CanMessageAsync(int id, CancellationToken ct) => AgreedRequests.AnyAsync(r => r.Id == id
        && (r.Status == MaintenanceRequestStatus.Accepted || r.Status == MaintenanceRequestStatus.InProgress)
        && ProviderEligibility.Active(db).Any(p => p.Id == r.ProviderProfileId), ct);

    public Task<bool> CanReviewAsync(int id, CancellationToken ct) => AgreedRequests.AnyAsync(r => r.Id == id
        && r.Status == MaintenanceRequestStatus.Completed && r.CompletedAtUtc != null
        && db.RequestQuotes.Any(q => q.MaintenanceRequestId == id && q.FinalPrice != null && q.FinalPriceAcceptedAtUtc != null), ct);
}
