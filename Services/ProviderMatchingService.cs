using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public sealed record ProviderMatch(
    int Id,
    string DisplayName,
    string Specialty,
    string City,
    string Area,
    string AvailabilityLabel,
    double? AverageRating,
    int ReviewCount)
{
    public string Reason =>
        $"{Specialty} · {Area} — {City} · {AvailabilityLabel} · " +
        (ReviewCount == 0 ? "لا توجد تقييمات بعد" : $"{AverageRating:0.0}/5 من {ReviewCount} تقييم");
}
public class ProviderMatchingService(ApplicationDbContext db)
{
    public virtual Task<IReadOnlyList<ProviderMatch>> FindAsync(int categoryId, int areaId, string? name, CancellationToken ct, string? ownerId = null)
    {
        var query = ProviderEligibility.ForRequest(db, categoryId, areaId, ownerId).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(p => p.DisplayName.Contains(name));
        return RankAsync(query, ct);
    }

    public virtual Task<IReadOnlyList<ProviderMatch>> FindInCityAsync(
        int categoryId,
        int cityId,
        int excludedAreaId,
        CancellationToken ct,
        string? ownerId = null)
    {
        var query = ProviderEligibility.Active(db).AsNoTracking().Where(provider =>
            provider.ServiceCategoryId == categoryId
            && provider.Area!.CityId == cityId
            && provider.AreaId != excludedAreaId
            && provider.UserId != ownerId);
        return RankAsync(query, ct);
    }

    private static async Task<IReadOnlyList<ProviderMatch>> RankAsync(
        IQueryable<ProviderProfile> query,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Rank only eligible service coverage. No provider coordinates exist, so no distance is fabricated.
        var candidates = await query.Select(p => new { p.Id, p.DisplayName, p.Availability, p.AvailableAfterUtc,
            Area = p.Area!.Name, City = p.Area.City.Name, Category = p.ServiceCategory!.Name,
            AvailabilityRank = p.Availability == ProviderAvailability.AvailableNow || (p.Availability == ProviderAvailability.AvailableAfter && p.AvailableAfterUtc <= now) ? 0 : p.Availability == ProviderAvailability.AvailableAfter ? 1 : 2,
            Rating = p.Reviews.Select(r => (double?)r.Rating).Average(), Count = p.Reviews.Count() })
            .OrderBy(p => p.AvailabilityRank).ThenByDescending(p => p.Rating ?? 0).ThenByDescending(p => p.Count).ThenBy(p => p.Id).Take(5).ToListAsync(ct);
        return candidates.Select(p => new ProviderMatch(
            p.Id,
            p.DisplayName,
            p.Category,
            p.City,
            p.Area,
            ProviderAvailabilityRules.Label(p.Availability, p.AvailableAfterUtc),
            p.Rating,
            p.Count)).ToList();
    }
}

