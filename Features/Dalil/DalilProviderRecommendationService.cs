using FixPal.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace FixPal.Features.Dalil;

public interface IDalilProviderRecommendationService
{
    Task<IReadOnlyList<DalilProviderRecommendation>> FindAsync(
        int categoryId,
        int areaId,
        int cityId,
        string cityName,
        string? ownerId,
        CancellationToken cancellationToken);
}

public sealed class DalilProviderRecommendationService(ProviderMatchingService matching)
    : IDalilProviderRecommendationService
{
    public async Task<IReadOnlyList<DalilProviderRecommendation>> FindAsync(
        int categoryId,
        int areaId,
        int cityId,
        string cityName,
        string? ownerId,
        CancellationToken cancellationToken)
    {
        var scope = DalilProviderMatchScope.ExactArea;
        var matches = await matching.FindAsync(categoryId, areaId, null, cancellationToken, ownerId);
        if (matches.Count == 0)
        {
            scope = DalilProviderMatchScope.SameCity;
            matches = await matching.FindInCityAsync(
                categoryId, cityId, areaId, cancellationToken, ownerId);
            matches = matches.Where(match =>
                string.Equals(match.City, cityName, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return matches.Select(match => new DalilProviderRecommendation(
            match.Id,
            match.DisplayName,
            match.Specialty,
            match.City,
            match.Area,
            match.AverageRating,
            match.ReviewCount,
            match.AvailabilityLabel,
            scope,
            $"/Providers/Details/{match.Id}",
            BuildRequestUrl(scope, match.Id, categoryId, cityId, areaId))).ToArray();
    }

    private static string BuildRequestUrl(
        DalilProviderMatchScope scope,
        int providerId,
        int categoryId,
        int cityId,
        int areaId)
    {
        var values = new Dictionary<string, string?>
        {
            ["categoryId"] = categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (scope == DalilProviderMatchScope.ExactArea)
            values["providerProfileId"] = providerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else
        {
            values["cityId"] = cityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            values["areaId"] = areaId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return QueryHelpers.AddQueryString("/MaintenanceRequests/Create", values);
    }
}
