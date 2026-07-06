using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PackForge.Web.Observability;

/// <summary>Readiness probe dependency: can we reach blob storage?</summary>
public class BlobHealthCheck(BlobServiceClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await client.GetPropertiesAsync(ct);
            return HealthCheckResult.Healthy("Blob storage reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Blob storage unreachable.", ex);
        }
    }
}
