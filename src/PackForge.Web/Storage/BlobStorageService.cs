using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PackForge.Web.Storage;

public class BlobStorageService(BlobServiceClient client)
{
    public const string ModelsContainer = "models";

    /// <summary>Dev-only bootstrap: containers + permissive CORS so the browser can PUT directly to Azurite.</summary>
    public async Task InitializeDevAsync(CancellationToken ct = default)
    {
        await client.GetBlobContainerClient(ModelsContainer).CreateIfNotExistsAsync(cancellationToken: ct);

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
        => GetSasUri(blobName, BlobSasPermissions.Create | BlobSasPermissions.Write, validity ?? TimeSpan.FromMinutes(30));

    public Uri GetDownloadSasUri(string blobName, TimeSpan? validity = null)
        => GetSasUri(blobName, BlobSasPermissions.Read, validity ?? TimeSpan.FromMinutes(10));

    private Uri GetSasUri(string blobName, BlobSasPermissions permissions, TimeSpan validity)
    {
        var blob = client.GetBlobContainerClient(ModelsContainer).GetBlobClient(blobName);
        return blob.GenerateSasUri(permissions, DateTimeOffset.UtcNow.Add(validity));
    }

    public async Task<BlobProperties?> GetBlobPropertiesAsync(string blobName, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(ModelsContainer).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(ct))
            return null;
        return (await blob.GetPropertiesAsync(cancellationToken: ct)).Value;
    }

    /// <summary>Streams the blob through SHA-256 with constant memory — the server verifies, it never buffers.</summary>
    public async Task<string> ComputeSha256Async(string blobName, CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(ModelsContainer).GetBlobClient(blobName);
        await using var stream = await blob.OpenReadAsync(cancellationToken: ct);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}
