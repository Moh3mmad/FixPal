using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FixPal.Services;

public sealed class BlobStorageOptions
{
    public const string SectionName = "Storage";

    public string? BlobServiceUri { get; set; }

    public bool TryGetBlobServiceUri(out Uri? uri) =>
        Uri.TryCreate(BlobServiceUri, UriKind.Absolute, out uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host);

    public Uri GetBlobServiceUri() => TryGetBlobServiceUri(out var uri)
        ? uri!
        : throw new InvalidOperationException(
            "Storage:BlobServiceUri must be an absolute HTTPS Azure Blob service URI.");
}

// Production adapter base. The application keeps every blob private and serves
// media through the existing controller authorization checks.
public abstract partial class AzureBlobMediaStorage(BlobServiceClient serviceClient)
{
    private const int MaxBytes = LocalPrivateMediaStorage.MaxBytes;

    protected abstract string ContainerName { get; }

    [GeneratedRegex("\\A[a-f0-9]{32}\\.(png|jpg)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKey();

    protected async Task<StoredMedia> SaveAsyncCore(IFormFile file, CancellationToken ct)
    {
        var image = await ImageUploadValidation.ReadAsync(file, ct);
        if (image.Bytes.LongLength > MaxBytes) throw new InvalidDataException("الصورة أكبر من الحد المسموح.");

        var key = Guid.NewGuid().ToString("N") + (image.ContentType == "image/png" ? ".png" : ".jpg");
        var container = serviceClient.GetBlobContainerClient(ContainerName);
        try
        {
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            // Existing containers are also forced private before any media is written.
            await container.SetAccessPolicyAsync(PublicAccessType.None, cancellationToken: ct);
            var blob = container.GetBlobClient(key);
            await using var content = new MemoryStream(image.Bytes, writable: false);
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = image.ContentType }
            }, ct);
        }
        catch (RequestFailedException ex)
        {
            throw new IOException("Azure Blob storage operation failed.", ex);
        }
        return new StoredMedia(key, image.ContentType, image.Bytes.LongLength);
    }

    protected async Task<Stream> OpenAsyncCore(string key, CancellationToken ct)
    {
        var blob = BlobFor(key);
        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            throw new FileNotFoundException("Stored media was not found.", key, ex);
        }
        catch (RequestFailedException ex)
        {
            throw new IOException("Azure Blob storage operation failed.", ex);
        }
    }

    protected async Task DeleteAsyncCore(string key, CancellationToken ct)
    {
        var blob = BlobFor(key);
        try
        {
            await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            throw new IOException("Azure Blob storage operation failed.", ex);
        }
    }

    private BlobClient BlobFor(string key) => SafeKey().IsMatch(key)
        ? serviceClient.GetBlobContainerClient(ContainerName).GetBlobClient(key)
        : throw new InvalidDataException("Invalid storage key.");
}

public sealed class AzureBlobPrivateMediaStorage(BlobServiceClient serviceClient)
    : AzureBlobMediaStorage(serviceClient), IPrivateMediaStorage
{
    protected override string ContainerName => "request-evidence";

    public Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct) => SaveAsyncCore(file, ct);
    public Task<Stream> OpenAsync(string key, CancellationToken ct) => OpenAsyncCore(key, ct);
    public Task DeleteAsync(string key, CancellationToken ct) => DeleteAsyncCore(key, ct);
}

public sealed class AzureBlobPortfolioMediaStorage(BlobServiceClient serviceClient)
    : AzureBlobMediaStorage(serviceClient), IPortfolioMediaStorage
{
    protected override string ContainerName => "provider-portfolio";

    public Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct) => SaveAsyncCore(file, ct);
    public Task<Stream> OpenAsync(string key, CancellationToken ct) => OpenAsyncCore(key, ct);
    public Task DeleteAsync(string key, CancellationToken ct) => DeleteAsyncCore(key, ct);
}
