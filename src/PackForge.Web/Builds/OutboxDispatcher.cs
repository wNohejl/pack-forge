using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using PackForge.Web.Data;

namespace PackForge.Web.Builds;

/// <summary>
/// Relays transactional-outbox rows to their queue. Producers write an OutboxMessage
/// in the same transaction as their domain change; this dispatcher is the only thing
/// that talks to the queue, so enqueue is guaranteed-eventual even if a producer
/// crashes right after committing. At-least-once delivery — downstream consumers
/// (the build worker) are idempotent.
/// </summary>
public class OutboxDispatcher(
    QueueClient queue,
    IDbContextFactory<PackForgeDbContext> dbFactory,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        while (!ct.IsCancellationRequested)
        {
            int sent = 0;
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var pending = await db.OutboxMessages
                    .Where(m => m.SentAt == null)
                    .OrderBy(m => m.Id)
                    .Take(32)
                    .ToListAsync(ct);

                foreach (var msg in pending)
                {
                    await queue.SendMessageAsync(msg.Body, ct);
                    msg.SentAt = DateTimeOffset.UtcNow;
                    msg.Attempts++;
                    sent++;
                }
                if (sent > 0)
                    await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(sent > 0 ? 200 : 1000), ct);
        }
    }
}
