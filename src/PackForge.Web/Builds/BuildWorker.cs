using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PackForge.Core;
using PackForge.Core.Models;
using PackForge.Core.Packaging;
using PackForge.Web.Data;
using PackForge.Web.Observability;
using PackForge.Web.Realtime;
using PackForge.Web.Storage;

namespace PackForge.Web.Builds;

/// <summary>
/// Queue-driven package builder. Locally this is a hosted service polling the
/// Azurite queue; in Azure the same loop body becomes an ACA Job triggered by
/// queue depth (KEDA). Web request latency never depends on build time.
/// </summary>
public class BuildWorker(
    QueueClient queue,
    [FromKeyedServices(BuildWorker.PoisonQueueKey)] QueueClient poisonQueue,
    IDbContextFactory<PackForgeDbContext> dbFactory,
    BlobStorageService blobs,
    ProgressNotifier progress,
    ILogger<BuildWorker> logger) : BackgroundService
{
    public const string QueueName = "builds";
    public const string PoisonQueueName = "builds-poison";
    public const string PoisonQueueKey = "poison";
    private const int MaxDequeueCount = 5; // after this many failed attempts, dead-letter it

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        await poisonQueue.CreateIfNotExistsAsync(cancellationToken: ct);
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
                // Poison guard: a message we've already choked on too many times gets
                // dead-lettered so it can't block the queue or retry forever.
                if (msg.DequeueCount > MaxDequeueCount)
                {
                    await DeadLetterAsync(msg, "exceeded max dequeue count", ct);
                    continue;
                }

                if (!Guid.TryParse(msg.Body.ToString(), out var buildId))
                {
                    await DeadLetterAsync(msg, "unparseable body", ct);
                    continue;
                }

                try
                {
                    await ProcessAsync(buildId, ct);
                    await queue.DeleteMessageAsync(msg.MessageId, msg.PopReceipt, ct); // success → remove
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Transient failure: leave the message so it redelivers after the
                    // visibility timeout. DequeueCount climbs; the guard above eventually DLQs it.
                    logger.LogWarning(ex, "Build {BuildId} attempt {Attempt} failed; will retry", buildId, msg.DequeueCount);
                }
            }
        }
    }

    private async Task DeadLetterAsync(Azure.Storage.Queues.Models.QueueMessage msg, string reason, CancellationToken ct)
    {
        logger.LogError("Dead-lettering build message {MessageId} ({Reason}): {Body}", msg.MessageId, reason, msg.Body.ToString());
        await poisonQueue.SendMessageAsync(msg.MessageText, ct);
        if (Guid.TryParse(msg.Body.ToString(), out var buildId))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var build = await db.PackageBuilds.FindAsync([buildId], ct);
            if (build is not null && build.Status != BuildStatus.Ready)
            {
                build.Status = BuildStatus.Failed;
                build.Error = $"Dead-lettered: {reason}";
                build.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                await progress.BuildsChangedAsync();
            }
        }
        await queue.DeleteMessageAsync(msg.MessageId, msg.PopReceipt, ct);
    }

    private async Task ProcessAsync(Guid buildId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var build = await db.PackageBuilds.FindAsync([buildId], ct);
        if (build is null || build.Status is BuildStatus.Ready)
            return;

        build.Status = BuildStatus.Building;
        await db.SaveChangesAsync(ct);
        await progress.BuildsChangedAsync();

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
            // Reset to Queued and rethrow — the receive loop leaves the message for
            // redelivery (transient), or dead-letters it once DequeueCount is exhausted.
            build.Status = BuildStatus.Queued;
            build.Error = ex.Message;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await db.SaveChangesAsync(ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
        await progress.BuildsChangedAsync();
    }

    private static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        return sb.Length == 0 ? "package" : sb.ToString();
    }
}
