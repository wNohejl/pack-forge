using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PackForge.Web.Storage;

public class BlobStorageService(BlobServiceClient client)
{
    public const string ModelsContainer = "models";
    public const string PackagesContainer = "packages";
    public const string MigratedContainer = "migrated";

    /// <summary>Dev-only bootstrap: containers + permissive CORS so the browser can PUT directly to Azurite.</summary>
    public async Task InitializeDevAsync(CancellationToken ct = default)
    {
        await client.GetBlobContainerClient(ModelsContainer).CreateIfNotExistsAsync(cancellationToken: ct);
        await client.GetBlobContainerClient(PackagesContainer).CreateIfNotExistsAsync(cancellationToken: ct);
        await client.GetBlobContainerClient(MigratedContainer).CreateIfNotExistsAsync(cancellationToken: ct);

        var props = await client.GetPropertiesAsync(ct);
        props.Value.Cors = new List<BlobCorsRule>
        {
            new()
            {
                AllowedOrigins = "*",
                AllowedMethods = "GET,PUT,HEAD,OPTIONS",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600,
            },
        };
        await client.SetPropertiesAsync(props.Value, ct);
    }

    public Uri GetUploadSasUri(string blobName, TimeSpan? validity = null)
        => GetSasUri(ModelsContainer, blobName, BlobSasPermissions.Create | BlobSasPermissions.Write, validity ?? TimeSpan.FromMinutes(30));

    public Uri GetDownloadSasUri(string container, string blobName, TimeSpan? validity = null)
        => GetSasUri(container, blobName, BlobSasPermissions.Read, validity ?? TimeSpan.FromMinutes(10));

    private Uri GetSasUri(string container, string blobName, BlobSasPermissions permissions, TimeSpan validity)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        return blob.GenerateSasUri(permissions, DateTimeOffset.UtcNow.Add(validity));
    }

    public async Task<BlobProperties?> GetBlobPropertiesAsync(string container, string blobName, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(ct))
            return null;
        return (await blob.GetPropertiesAsync(cancellationToken: ct)).Value;
    }

    public async Task<string> DownloadTextAsync(string container, string blobName, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        var result = await blob.DownloadContentAsync(ct);
        return result.Value.Content.ToString();
    }

    public async Task UploadStreamAsync(string container, string blobName, Stream stream, string contentType, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            TransferOptions = new Azure.Storage.StorageTransferOptions { MaximumConcurrency = 1 }, // sequential: source stream is hashed as it's read
        }, ct);
    }

    public async Task UploadBytesAsync(string container, string blobName, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
    }

    /// <summary>Streams the blob through SHA-256 with constant memory — the server verifies, it never buffers.</summary>
    public async Task<string> ComputeSha256Async(string container, string blobName, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(blobName);
        await using var stream = await blob.OpenReadAsync(cancellationToken: ct);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}
