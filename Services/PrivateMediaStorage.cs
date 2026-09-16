using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
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
        try
        {
            var options = new DecoderOptions { SkipMetadata = true, MaxFrames = 1 };
            using var source = new MemoryStream(bytes, writable: false);
            var format = await Image.DetectFormatAsync(source, ct);
            if (format.Name is not ("PNG" or "JPEG")) throw new InvalidDataException("اختر صورة PNG أو JPEG صحيحة.");
            source.Position = 0;
            var info = await Image.IdentifyAsync(options, source, ct);
            if (info.Width is <= 0 or > 12000 || info.Height is <= 0 or > 12000 || (long)info.Width * info.Height > 40000000)
                throw new InvalidDataException("أبعاد الصورة أكبر من الحد المسموح.");
            source.Position = 0;
            using var decoded = await Image.LoadAsync(options, source, ct);
            // Store re-encoded pixels, without EXIF/GPS or unparsed trailing payloads.
            using var normalized = new MemoryStream();
            await decoded.SaveAsync(normalized, format.Name == "PNG" ? new PngEncoder() : new JpegEncoder { Quality = 85 }, ct);
            if (normalized.Length > MaxBytes) throw new InvalidDataException("الصورة بعد المعالجة أكبر من 5 ميغابايت.");
            return new(normalized.ToArray(), format.Name == "PNG" ? "image/png" : "image/jpeg");
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new InvalidDataException("تعذر قراءة الصورة. اختر ملف PNG أو JPEG سليمًا.", ex);
        }
    }
}
