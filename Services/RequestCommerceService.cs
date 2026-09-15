using System.Data;
using System.Security.Claims;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public class RequestCommerceService(ApplicationDbContext db, RequestAccessService access)
{
    public static bool ValidAmount(decimal amount) => amount > 0 && amount <= 1000000m && decimal.Round(amount, 2) == amount;
    public async Task<bool> SubmitQuoteAsync(ClaimsPrincipal user, int id, QuoteInputModel input, CancellationToken ct)
    {
        if (!ValidAmount(input.MinimumPrice) || !ValidAmount(input.MaximumPrice) || input.MaximumPrice < input.MinimumPrice || input.Note?.Length > 500) return false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grant = await access.GetAsync(user, id, ct);
        if (grant is not { IsProvider: true } || grant.Request.RequestType != RequestType.PrivateService || grant.Request.Status is MaintenanceRequestStatus.Completed or MaintenanceRequestStatus.Cancelled) return false;
        if (await db.RequestQuotes.AnyAsync(q => q.MaintenanceRequestId == id, ct)) return false;
        db.RequestQuotes.Add(new() { MaintenanceRequestId = id, ProviderProfileId = grant.Request.ProviderProfileId!.Value,
            MinimumPrice = input.MinimumPrice, MaximumPrice = input.MaximumPrice, Note = input.Note?.Trim(), SubmittedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<bool> AcceptQuoteAsync(ClaimsPrincipal user, int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grant = await access.GetAsync(user, id, ct);
        if (grant is not { IsOwner: true } || grant.Request.Status is MaintenanceRequestStatus.Completed or MaintenanceRequestStatus.Cancelled) return false;
        var count = await db.RequestQuotes.Where(q => q.MaintenanceRequestId == id && q.ProviderProfileId == grant.Request.ProviderProfileId && q.AcceptedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.AcceptedAtUtc, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct); return count == 1;
    }
    public async Task<bool> ProposeFinalAsync(ClaimsPrincipal user, int id, decimal amount, CancellationToken ct)
    {
        if (!ValidAmount(amount)) return false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grant = await access.GetAsync(user, id, ct);
        if (grant is not { IsProvider: true } || grant.Request.Status != MaintenanceRequestStatus.InProgress) return false;
        var changed = await db.RequestQuotes.Where(q => q.MaintenanceRequestId == id && q.ProviderProfileId == grant.Request.ProviderProfileId
            && q.AcceptedAtUtc != null && q.FinalPrice == null && amount >= q.MinimumPrice && amount <= q.MaximumPrice)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.FinalPrice, amount), ct);
        await tx.CommitAsync(ct); return changed == 1;
    }
    public async Task<bool> AcceptFinalAsync(ClaimsPrincipal user, int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grant = await access.GetAsync(user, id, ct);
        if (grant is not { IsOwner: true } || grant.Request.Status != MaintenanceRequestStatus.InProgress) return false;
        var changed = await db.RequestQuotes.Where(q => q.MaintenanceRequestId == id && q.FinalPrice != null && q.FinalPriceAcceptedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.FinalPriceAcceptedAtUtc, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct); return changed == 1;
    }
}
