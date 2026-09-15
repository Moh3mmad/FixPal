using Microsoft.EntityFrameworkCore;
namespace FixPal.Models.ViewModels;
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; } = 12;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public static async Task<PagedResult<T>> CreateAsync(IQueryable<T> orderedQuery, int page, CancellationToken ct, int pageSize = 12)
    {
        var count = await orderedQuery.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)));
        return new() { Items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), Page = page, TotalCount = count, PageSize = pageSize };
    }
}
