namespace PackForge.Core;

/// <summary>
/// Transactional outbox row. Written in the same DB transaction as the work it
/// refers to (e.g. a queued build), then relayed to the queue by a dispatcher.
/// This makes "record the build" and "enqueue the build" atomic — no orphaned
/// builds if the process dies between the DB commit and the queue send.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
}
