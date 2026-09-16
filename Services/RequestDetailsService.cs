using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public class RequestDetailsService(ApplicationDbContext db, RequestAccessService access, RequestAgreementPolicy agreement)
{
    public async Task<RequestDetailsViewModel?> GetAsync(ClaimsPrincipal user, int id, int quotePage, CancellationToken ct)
    {
        var grant = await access.GetAsync(user, id, ct);
        if (grant == null) return null;
        var model = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Id == id).Select(RequestDetailsViewModel.DetailProjection).SingleAsync(ct);
        model.IsOwner = grant.IsOwner;
        model.CanManage = grant.IsProvider && !grant.IsOwner
            && await ProviderEligibility.ForRequest(db, grant.Request.ServiceCategoryId, grant.Request.AreaId, grant.Request.CustomerId).AnyAsync(p => p.Id == grant.Request.ProviderProfileId, ct);
        model.Quote = await db.RequestQuotes.AsNoTracking().Include(q => q.CurrentRevision).Include(q => q.AcceptedRevision).SingleOrDefaultAsync(q => q.MaintenanceRequestId == id, ct);
        model.Review = await db.ProviderReviews.AsNoTracking().SingleOrDefaultAsync(r => r.MaintenanceRequestId == id, ct);
        model.HasAgreement = await agreement.AgreedRequests.AnyAsync(r => r.Id == id, ct);
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
