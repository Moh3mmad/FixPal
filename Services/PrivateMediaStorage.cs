using System.Buffers.Binary;
using System.Text.RegularExpressions;
namespace FixPal.Services;
public record StoredMedia(string Key, string ContentType, long Size);
public interface IPrivateMediaStorage
{
    Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct);
    Task<Stream> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
// Development adapter. Replace with private object storage; never expose the root as static files.
public partial class LocalPrivateMediaStorage(IWebHostEnvironment environment) : IPrivateMediaStorage
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "RequestEvidence");
    [GeneratedRegex("^[a-f0-9]{32}\\.(png|jpg)$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKey();
    private string PathFor(string key) => SafeKey().IsMatch(key) ? Path.Combine(Root, key) : throw new InvalidDataException("Invalid storage key.");
    public async Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var picture = await ImageUploadValidation.ReadAsync(file, ct);
        var bytes = picture.Bytes;
        var png = picture.ContentType == "image/png";
        var key = Guid.NewGuid().ToString("N") + (png ? ".png" : ".jpg");
        Directory.CreateDirectory(Root);
        try { await File.WriteAllBytesAsync(PathFor(key), bytes, ct); } catch { File.Delete(PathFor(key)); throw; }
        return new(key, png ? "image/png" : "image/jpeg", bytes.Length);
    }
    public Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous));
    }
    public Task DeleteAsync(string key, CancellationToken ct) { File.Delete(PathFor(key)); return Task.CompletedTask; }
}

public record ValidatedImage(byte[] Bytes, string ContentType);
public static class ImageUploadValidation
{
    private const int MaxBytes = LocalPrivateMediaStorage.MaxBytes;
    public static async Task<ValidatedImage> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxBytes) throw new InvalidDataException("الحد الأقصى للصورة 5 ميغابايت.");
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidDataException("الصورة أكبر من الحد المسموح.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        var png = bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            && bytes.AsSpan(12,4).SequenceEqual("IHDR"u8);
        var jpeg = bytes.Length >= 20 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217;
        if (!png && !jpeg) throw new InvalidDataException("اختر صورة PNG أو JPEG صحيحة؛ الامتداد وحده لا يكفي.");
        if (png)
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4));
            if (width == 0 || height == 0 || width > 12000 || height > 12000 || (long)width * height > 40000000)
                throw new InvalidDataException("أبعاد الصورة أكبر من الحد المسموح.");
        }
        return new(bytes, png ? "image/png" : "image/jpeg");
    }
}

