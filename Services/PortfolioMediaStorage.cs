using System.Text.RegularExpressions;

namespace FixPal.Services;

public interface IPortfolioMediaStorage
{
    Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct);
    Task<Stream> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

// Separate from request evidence. Public access is through the portfolio image
// endpoint, which checks visibility; this directory is never mounted as static files.
public partial class PortfolioMediaStorage(IWebHostEnvironment environment) : IPortfolioMediaStorage
{
    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "ProviderPortfolio");
    [GeneratedRegex("\\A[a-f0-9]{32}\\.(png|jpg)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKey();
    private string PathFor(string key) => SafeKey().IsMatch(key) ? Path.Combine(Root, key) : throw new InvalidDataException("Invalid portfolio storage key.");
    public async Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var image = await ImageUploadValidation.ReadAsync(file, ct);
        var key = Guid.NewGuid().ToString("N") + (image.ContentType == "image/png" ? ".png" : ".jpg");
        Directory.CreateDirectory(Root);
        try { await File.WriteAllBytesAsync(PathFor(key), image.Bytes, ct); }
        catch { File.Delete(PathFor(key)); throw; }
        return new(key, image.ContentType, image.Bytes.Length);
    }
    public Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous));
    }
    // Used only to clean up failed creation, never by archive.
    public Task DeleteAsync(string key, CancellationToken ct) { File.Delete(PathFor(key)); return Task.CompletedTask; }
}
