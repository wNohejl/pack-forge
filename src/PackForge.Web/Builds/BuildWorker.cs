using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using PackForge.Core;
using PackForge.Core.Models;
using PackForge.Core.Packaging;
using PackForge.Web.Data;
using PackForge.Web.Observability;
using PackForge.Web.Storage;

namespace PackForge.Web.Builds;

/// <summary>
/// Queue-driven package builder. Locally this is a hosted service polling the
/// Azurite queue; in Azure the same loop body becomes an ACA Job triggered by
/// queue depth (KEDA). Web request latency never depends on build time.
/// </summary>
public class BuildWorker(
    QueueClient queue,
    IDbContextFactory<PackForgeDbContext> dbFactory,
    BlobStorageService blobs,
    ILogger<BuildWorker> logger) : BackgroundService
{
    public const string QueueName = "builds";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        while (!ct.IsCancellationRequested)
        {
            var messages = await queue.ReceiveMessagesAsync(maxMessages: 8, visibilityTimeout: TimeSpan.FromMinutes(5), ct);
            if (messages.Value.Length == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            foreach (var msg in messages.Value)
            {
                if (Guid.TryParse(msg.Body.ToString(), out var buildId))
                    await ProcessAsync(buildId, ct);
                else
                    logger.LogWarning("Discarding malformed build message: {Body}", msg.Body.ToString());
                await queue.DeleteMessageAsync(msg.MessageId, msg.PopReceipt, ct);
            }
        }
    }

    private async Task ProcessAsync(Guid buildId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var build = await db.PackageBuilds.FindAsync([buildId], ct);
        if (build is null || build.Status is BuildStatus.Ready)
            return;

        build.Status = BuildStatus.Building;
        await db.SaveChangesAsync(ct);

        using var activity = Telemetry.Source.StartActivity("build-package");
        activity?.SetTag("model.name", build.ModelName);
        activity?.SetTag("package.version", build.Version);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var upload = await db.Uploads.FindAsync([build.UploadId], ct)
                ?? throw new InvalidOperationException($"Upload {build.UploadId} not found.");

            var json = await blobs.DownloadTextAsync(BlobStorageService.ModelsContainer, upload.BlobName, ct);
            var model = ModelDefinition.FromJson(json);

            var bytes = PackageBuilder.Build(model, build.ModelSha256, build.Version);
            build.BlobName = $"{build.Id}/{SafeName(model.Name)}-v{build.Version}.zip";
            await blobs.UploadBytesAsync(BlobStorageService.PackagesContainer, build.BlobName, bytes, "application/zip", ct);

            build.PackageSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            build.Status = BuildStatus.Ready;
            build.CompletedAt = DateTimeOffset.UtcNow;
            Telemetry.PackagesBuilt.Add(1);
            Telemetry.BuildDurationMs.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            logger.LogInformation("Built package {Name} v{Version} ({Sha})", build.ModelName, build.Version, build.PackageSha256[..12]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            build.Status = BuildStatus.Failed;
            build.Error = ex.Message;
            build.CompletedAt = DateTimeOffset.UtcNow;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Build {BuildId} failed", buildId);
        }

        await db.SaveChangesAsync(ct);
    }

    private static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        return sb.Length == 0 ? "package" : sb.ToString();
    }
}
