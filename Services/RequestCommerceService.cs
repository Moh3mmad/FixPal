using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;

public class RequestCommerceService(ApplicationDbContext db, RequestAccessService access,
    RequestMutationService mutations, RequestAgreementPolicy agreement)
{
    public static bool ValidAmount(decimal amount) => amount > 0 && amount <= 1000000m && decimal.Round(amount, 2) == amount;

    private Task<bool> EligibleAsync(RequestAccess grant, CancellationToken ct) =>
        ProviderEligibility.ForRequest(db, grant.Request.ServiceCategoryId, grant.Request.AreaId, grant.Request.CustomerId)
            .AnyAsync(p => p.Id == grant.Request.ProviderProfileId, ct);

    public Task<MutationResult> SubmitQuoteAsync(ClaimsPrincipal user, int id, QuoteInputModel input, CancellationToken ct) =>
        mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(user, id, ct);
            if (grant is not { IsProvider: true, IsOwner: false }) return MutationResult.NotFound;
            if (grant.Request.RequestType != RequestType.PrivateService || grant.Request.Status != MaintenanceRequestStatus.Accepted
                || !await EligibleAsync(grant, ct) || !ValidAmount(input.MinimumPrice) || !ValidAmount(input.MaximumPrice)
                || input.MaximumPrice < input.MinimumPrice || input.Note?.Length > 500) return MutationResult.Conflict;
            var quote = await db.RequestQuotes.SingleOrDefaultAsync(q => q.MaintenanceRequestId == id, ct);
            if (quote != null && (quote.ProviderProfileId != grant.Request.ProviderProfileId
                || quote.State is not (QuoteState.Rejected or QuoteState.RevisionRequested)
                || quote.CurrentRevisionNumber != input.ExpectedRevisionNumber)) return MutationResult.Conflict;
            if (quote == null)
            {
                if (input.ExpectedRevisionNumber != null) return MutationResult.Conflict;
                quote = new RequestQuote { MaintenanceRequestId = id, ProviderProfileId = grant.Request.ProviderProfileId!.Value,
                    MinimumPrice = input.MinimumPrice, MaximumPrice = input.MaximumPrice, Note = input.Note?.Trim(), SubmittedAtUtc = DateTime.UtcNow };
                db.RequestQuotes.Add(quote);
                await db.SaveChangesAsync(ct);
            }
            var number = (quote.CurrentRevisionNumber ?? 0) + 1;
            db.QuoteRevisions.Add(new QuoteRevision { RequestQuoteId = quote.Id, Number = number,
                ProviderAuthorId = user.FindFirstValue(ClaimTypes.NameIdentifier)!, MinimumPrice = input.MinimumPrice,
                MaximumPrice = input.MaximumPrice, Note = input.Note?.Trim(), CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            quote.CurrentRevisionNumber = number;
            quote.State = QuoteState.AwaitingCustomer;
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);

    public Task<MutationResult> DecideAsync(ClaimsPrincipal user, int id, int revisionNumber, QuoteState decision, string? note, CancellationToken ct) =>
        mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(user, id, ct);
            if (grant is not { IsOwner: true, IsProvider: false }) return MutationResult.NotFound;
            if (grant.Request.RequestType != RequestType.PrivateService || grant.Request.Status != MaintenanceRequestStatus.Accepted
                || grant.Request.AcceptedAtUtc == null || !await EligibleAsync(grant, ct)
                || revisionNumber <= 0 || note?.Length > 500 || decision is not (QuoteState.Accepted or QuoteState.Rejected or QuoteState.RevisionRequested))
                return MutationResult.Conflict;
            var quote = await db.RequestQuotes.SingleOrDefaultAsync(q => q.MaintenanceRequestId == id, ct);
            if (quote == null || quote.ProviderProfileId != grant.Request.ProviderProfileId || quote.State != QuoteState.AwaitingCustomer
                || quote.CurrentRevisionNumber != revisionNumber || quote.AcceptedRevisionNumber != null) return MutationResult.Conflict;
            var now = DateTime.UtcNow;
            db.QuoteDecisions.Add(new QuoteDecision { RequestQuoteId = quote.Id, RevisionNumber = revisionNumber, State = decision,
                CustomerAuthorId = grant.Request.CustomerId, Note = note?.Trim(), CreatedAtUtc = now });
            quote.State = decision;
            if (decision == QuoteState.Accepted) { quote.AcceptedRevisionNumber = revisionNumber; quote.AcceptedAtUtc = now; }
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);

    public Task<MutationResult> ProposeFinalAsync(ClaimsPrincipal user, int id, decimal amount, CancellationToken ct) =>
        mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(user, id, ct);
            if (grant is not { IsProvider: true, IsOwner: false }) return MutationResult.NotFound;
            if (!ValidAmount(amount) || grant.Request.Status != MaintenanceRequestStatus.InProgress || !await EligibleAsync(grant, ct)
                || !await agreement.AgreedRequests.AnyAsync(r => r.Id == id, ct)) return MutationResult.Conflict;
            var quote = await db.RequestQuotes.Include(q => q.AcceptedRevision).SingleAsync(q => q.MaintenanceRequestId == id, ct);
            if (quote.FinalPrice != null || quote.AcceptedRevision == null || amount < quote.AcceptedRevision.MinimumPrice || amount > quote.AcceptedRevision.MaximumPrice)
                return MutationResult.Conflict;
            quote.FinalPrice = amount;
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);

    public Task<MutationResult> AcceptFinalAsync(ClaimsPrincipal user, int id, CancellationToken ct) =>
        mutations.RunAsync(id, async () =>
        {
            var grant = await access.GetAsync(user, id, ct);
            if (grant is not { IsOwner: true, IsProvider: false }) return MutationResult.NotFound;
            if (grant.Request.Status != MaintenanceRequestStatus.InProgress || !await EligibleAsync(grant, ct)
                || !await agreement.AgreedRequests.AnyAsync(r => r.Id == id, ct)) return MutationResult.Conflict;
            var quote = await db.RequestQuotes.SingleAsync(q => q.MaintenanceRequestId == id, ct);
            if (quote.FinalPrice == null || quote.FinalPriceAcceptedAtUtc != null) return MutationResult.Conflict;
            quote.FinalPriceAcceptedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return MutationResult.Success;
        }, ct);
}
