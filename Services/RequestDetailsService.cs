using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public class RequestDetailsService(ApplicationDbContext db, RequestAccessService access, RequestAgreementPolicy agreement, RequestCommunicationPolicy communication)
{
    public async Task<RequestDetailsViewModel?> GetAsync(ClaimsPrincipal user, int id, int quotePage, CancellationToken ct)
    {
        var grant = await access.GetAsync(user, id, ct);
        if (grant == null) return null;
        var model = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Id == id).Select(RequestDetailsViewModel.DetailProjection).SingleAsync(ct);
        model.Communication = await communication.GetAsync(grant, user, ct);
        model.IsOwner = grant.IsOwner;
        model.CanManage = grant.IsProvider && !grant.IsOwner
            && await ProviderEligibility.ForRequest(db, grant.Request.ServiceCategoryId, grant.Request.AreaId, grant.Request.CustomerId).AnyAsync(p => p.Id == grant.Request.ProviderProfileId, ct);
        model.Quote = await db.RequestQuotes.AsNoTracking().Include(q => q.CurrentRevision).Include(q => q.AcceptedRevision).SingleOrDefaultAsync(q => q.MaintenanceRequestId == id, ct);
        model.Review = await db.ProviderReviews.AsNoTracking().SingleOrDefaultAsync(r => r.MaintenanceRequestId == id, ct);
        model.HasAgreement = await agreement.AgreedRequests.AnyAsync(r => r.Id == id, ct);
        // Never put private coordinates in the general details projection.
        // Recheck current ownership, assignment, agreement and state in the location query.
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var location = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Id == id
            && (r.CustomerId == actorId || (grant.IsProvider
                && r.ProviderProfile != null && r.ProviderProfile.UserId == actorId
                && (r.Status == MaintenanceRequestStatus.Accepted || r.Status == MaintenanceRequestStatus.InProgress)
                && agreement.AgreedRequests.Any(a => a.Id == r.Id))))
            .Select(r => new { r.Latitude, r.Longitude }).SingleOrDefaultAsync(ct);
        model.CanViewProblemLocation = location != null;
        if (location?.Latitude is double latitude && location.Longitude is double longitude
            && double.IsFinite(latitude) && double.IsFinite(longitude)
            && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        {
            model.Latitude = latitude;
            model.Longitude = longitude;
        }
        model.CanReview = grant.IsOwner && await agreement.CanReviewAsync(id, ct);
        model.CanDecideQuote = grant.IsOwner && !grant.IsProvider && model.Status == MaintenanceRequestStatus.Accepted
            && await ProviderEligibility.ForRequest(db, grant.Request.ServiceCategoryId, grant.Request.AreaId, grant.Request.CustomerId).AnyAsync(p => p.Id == grant.Request.ProviderProfileId, ct);
        model.QuoteHistory = await PagedResult<QuoteHistoryItem>.CreateAsync(db.QuoteRevisions.AsNoTracking()
            .Where(r => r.RequestQuote.MaintenanceRequestId == id).OrderByDescending(r => r.Number)
            .Select(r => new QuoteHistoryItem(r.Number, r.MinimumPrice, r.MaximumPrice, r.Note, r.CreatedAtUtc,
                db.QuoteDecisions.Where(d => d.RequestQuoteId == r.RequestQuoteId && d.RevisionNumber == r.Number).Select(d => (QuoteState?)d.State).SingleOrDefault(),
                db.QuoteDecisions.Where(d => d.RequestQuoteId == r.RequestQuoteId && d.RevisionNumber == r.Number).Select(d => d.Note).SingleOrDefault(),
                db.QuoteDecisions.Where(d => d.RequestQuoteId == r.RequestQuoteId && d.RevisionNumber == r.Number).Select(d => (DateTime?)d.CreatedAtUtc).SingleOrDefault())), quotePage, ct, 10);
        return model;
    }
}
