using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public record ProviderMatch(int Id, string DisplayName, string Reason, double? AverageRating, int ReviewCount);
public class ProviderMatchingService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<ProviderMatch>> FindAsync(int categoryId, int areaId, string? name, CancellationToken ct, string? ownerId = null)
    {
        var now = DateTime.UtcNow;
        var query = ProviderEligibility.ForRequest(db, categoryId, areaId, ownerId).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(p => p.DisplayName.Contains(name));
        // Rank only eligible service coverage. No provider coordinates exist, so no distance is fabricated.
        var candidates = await query.Select(p => new { p.Id, p.DisplayName, p.Availability, p.AvailableAfterUtc,
            Area = p.Area!.Name, City = p.Area.City.Name, Category = p.ServiceCategory!.Name,
            AvailabilityRank = p.Availability == ProviderAvailability.AvailableNow || (p.Availability == ProviderAvailability.AvailableAfter && p.AvailableAfterUtc <= now) ? 0 : p.Availability == ProviderAvailability.AvailableAfter ? 1 : 2,
            Rating = p.Reviews.Select(r => (double?)r.Rating).Average(), Count = p.Reviews.Count() })
            .OrderBy(p => p.AvailabilityRank).ThenByDescending(p => p.Rating ?? 0).ThenByDescending(p => p.Count).ThenBy(p => p.Id).Take(5).ToListAsync(ct);
        return candidates.Select(p => new ProviderMatch(p.Id, p.DisplayName,
            $"{p.Category} · {p.Area} — {p.City} · {ProviderAvailabilityRules.Label(p.Availability, p.AvailableAfterUtc)} · " +
            (p.Count == 0 ? "لا توجد تقييمات بعد" : $"{p.Rating:0.0}/5 من {p.Count} تقييم"), p.Rating, p.Count)).ToList();
    }
}

