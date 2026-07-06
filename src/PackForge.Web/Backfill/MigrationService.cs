using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PackForge.Core.Migration;
using PackForge.Web.Data;
using PackForge.Web.Storage;

namespace PackForge.Web.Backfill;

/// <summary>
/// The strangler backfill: inventory scan -> copy -> hash -> verify -> mark, idempotent
/// across runs. Throttling (429-style) from sources is retried with backoff; transfers
/// whose hashed byte count or blob hash disagree with the source are marked Failed,
/// never silently accepted.
/// </summary>
public class MigrationService(
    IEnumerable<IMigrationSource> sources,
    IDbContextFactory<PackForgeDbContext> dbFactory,
    BlobStorageService blobs,
    ILogger<MigrationService> logger)
{
    private readonly object gate = new();
    private Task? current;

    public bool Running => current is { IsCompleted: false };
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public long BytesCopied;

    public bool TryStart()
    {
        lock (gate)
        {
            if (Running)
                return false;
            StartedAt = DateTimeOffset.UtcNow;
            FinishedAt = null;
            Interlocked.Exchange(ref BytesCopied, 0);
            current = Task.Run(RunAsync);
            return true;
        }
    }

    public IMigrationSource? GetSource(string name) =>
        sources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private async Task RunAsync()
    {
        try
        {
            foreach (var source in sources)
                await InventoryAsync(source);

            await using var db = await dbFactory.CreateDbContextAsync();
            var work = await db.MigrationItems
                .Where(i => i.Status != MigrationStatus.Verified)
                .Select(i => i.Id)
                .ToListAsync();

            using var throttle = new SemaphoreSlim(4);
            await Task.WhenAll(work.Select(async id =>
            {
                await throttle.WaitAsync();
                try { await CopyAndVerifyAsync(id); }
                finally { throttle.Release(); }
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration run aborted");
        }
        finally
        {
            FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task InventoryAsync(IMigrationSource source)
    {
        var files = await WithBackoffAsync(() => source.EnumerateAsync(), $"enumerate {source.Name}");

        await using var db = await dbFactory.CreateDbContextAsync();
        var known = (await db.MigrationItems
                .Where(i => i.SourceSystem == source.Name)
                .Select(i => i.SourcePath)
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var f in files.Where(f => !known.Contains(f.RelativePath)))
        {
            db.MigrationItems.Add(new MigrationItem
            {
                Id = Guid.NewGuid(),
                SourceSystem = source.Name,
                SourcePath = f.RelativePath,
                SizeBytes = f.SizeBytes,
            });
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Inventory {Source}: {Count} files", source.Name, files.Count);
    }

    private async Task CopyAndVerifyAsync(Guid itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.MigrationItems.FindAsync(itemId);
        if (item is null || item.Status == MigrationStatus.Verified)
            return;
        var source = GetSource(item.SourceSystem);
        if (source is null)
            return;

        try
        {
            item.BlobName = $"{item.SourceSystem}/{item.SourcePath}";
            var (bytesRead, sourceSha) = await WithBackoffAsync(async () =>
            {
                await using var stream = await source.OpenReadAsync(item.SourcePath);
                await using var hashing = new HashingReadStream(stream);
                await blobs.UploadStreamAsync(BlobStorageService.MigratedContainer, item.BlobName, hashing, "application/octet-stream");
                return (hashing.BytesRead, hashing.FinalHashHex());
            }, $"copy {item.SourcePath}");

            item.SourceSha256 = sourceSha;
            item.Status = MigrationStatus.Copied;
            Interlocked.Add(ref BytesCopied, bytesRead);

            if (bytesRead != item.SizeBytes)
                throw new InvalidDataException($"Truncated transfer: expected {item.SizeBytes} bytes, received {bytesRead}.");

            item.BlobSha256 = await blobs.ComputeSha256Async(BlobStorageService.MigratedContainer, item.BlobName);
            if (item.BlobSha256 != item.SourceSha256)
                throw new InvalidDataException("Checksum mismatch between source and blob.");

            item.Status = MigrationStatus.Verified;
            item.VerifiedAt = DateTimeOffset.UtcNow;
            item.Error = null;
        }
        catch (Exception ex)
        {
            item.Status = MigrationStatus.Failed;
            item.Error = ex.Message;
        }

        await db.SaveChangesAsync();
    }

    private async Task<T> WithBackoffAsync<T>(Func<Task<T>> action, string what, int maxAttempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (SourceThrottledException ex) when (attempt < maxAttempts)
            {
                var delay = ex.RetryAfter + TimeSpan.FromMilliseconds(50 * attempt); // honor Retry-After + jittered growth
                logger.LogDebug("Throttled on {What} (attempt {Attempt}); backing off {Delay} ms", what, attempt, delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>Hashes bytes as they stream past — one read of the source covers both copy and hash.</summary>
    private sealed class HashingReadStream(Stream inner) : Stream
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long BytesRead { get; private set; }

        public string FinalHashHex() => Convert.ToHexStringLower(hash.GetHashAndReset());

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            if (n > 0)
            {
                hash.AppendData(buffer.AsSpan(offset, n));
                BytesRead += n;
            }
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            if (n > 0)
            {
                hash.AppendData(buffer.Span[..n]);
                BytesRead += n;
            }
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hash.Dispose();
                inner.Dispose();
            }
        }
    }
}
