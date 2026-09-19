using FixPal.Data;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Features.Dalil;

public sealed record DalilCategoryOption(int Id, string Name, string? Description);
public sealed record DalilAreaOption(int Id, string Name, int CityId, string CityName);

public sealed record DalilSafeCatalog(
    IReadOnlyList<DalilCategoryOption> Categories,
    IReadOnlyList<DalilAreaOption> Areas)
{
    public DalilSafeContext ToAssistantContext() => new(
        Categories.Select(category => new DalilPublicServiceCategory(category.Name, category.Description)).ToArray(),
        Areas.Select(area => new DalilPublicLocation(area.CityName, area.Name)).ToArray());

    public DalilCategoryOption? ResolveCategory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var resolved = Categories.Where(category => NamesEqual(category.Name, name)).Take(2).ToArray();
        return resolved.Length == 1 ? resolved[0] : null;
    }

    public DalilAreaOption? ResolveArea(string? cityName, string? areaName)
    {
        if (string.IsNullOrWhiteSpace(areaName)) return null;
        var matches = Areas.Where(area => NamesEqual(area.Name, areaName));
        if (!string.IsNullOrWhiteSpace(cityName))
            matches = matches.Where(area => NamesEqual(area.CityName, cityName));
        var resolved = matches.Take(2).ToArray();
        return resolved.Length == 1 ? resolved[0] : null;
    }

    private static bool NamesEqual(string actual, string proposed) =>
        string.Equals(actual.Trim(), proposed.Trim(), StringComparison.OrdinalIgnoreCase);
}

public interface IDalilSafeContextService
{
    Task<DalilSafeCatalog> GetAsync(CancellationToken cancellationToken);
}

public sealed class DalilSafeContextService(ApplicationDbContext db) : IDalilSafeContextService
{
    public async Task<DalilSafeCatalog> GetAsync(CancellationToken cancellationToken)
    {
        var categories = await db.ServiceCategories.AsNoTracking()
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new DalilCategoryOption(category.Id, category.Name, category.Description))
            .ToListAsync(cancellationToken);
        var areas = await db.Areas.AsNoTracking()
            .Where(area => area.City.IsActive)
            .OrderBy(area => area.City.Name)
            .ThenBy(area => area.Name)
            .ThenBy(area => area.Id)
            .Select(area => new DalilAreaOption(area.Id, area.Name, area.CityId, area.City.Name))
            .ToListAsync(cancellationToken);
        return new DalilSafeCatalog(categories, areas);
    }
}
