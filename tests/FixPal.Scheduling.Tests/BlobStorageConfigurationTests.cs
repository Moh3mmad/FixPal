using Azure.Identity;
using Azure.Storage.Blobs;
using FixPal.Services;
using Xunit;

namespace FixPal.Scheduling.Tests;

public sealed class BlobStorageConfigurationTests
{
    [Theory]
    [InlineData("https://tasliha.blob.core.windows.net", true)]
    [InlineData("http://tasliha.blob.core.windows.net", false)]
    [InlineData("not-a-uri", false)]
    [InlineData("", false)]
    public void BlobServiceUriRequiresAnAbsoluteHttpsUri(string value, bool expected)
    {
        var options = new BlobStorageOptions { BlobServiceUri = value };

        Assert.Equal(expected, options.TryGetBlobServiceUri(out _));
    }

    [Fact]
    public void InvalidConfigurationFailsClearly()
    {
        var options = new BlobStorageOptions { BlobServiceUri = "http://localhost" };

        var error = Assert.Throws<InvalidOperationException>(() => options.GetBlobServiceUri());
        Assert.Contains("Storage:BlobServiceUri", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AzureAdaptersRejectUnsafeKeysBeforeAnyStorageCall()
    {
        var client = new BlobServiceClient(
            new Uri("https://tasliha.blob.core.windows.net"), new DefaultAzureCredential());
        var evidence = new AzureBlobPrivateMediaStorage(client);
        var portfolio = new AzureBlobPortfolioMediaStorage(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => evidence.OpenAsync("../private.png", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => portfolio.DeleteAsync("not-a-media-key", default));
    }
}
