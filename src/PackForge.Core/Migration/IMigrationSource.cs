namespace PackForge.Core.Migration;

public readonly record struct SourceFile(string RelativePath, long SizeBytes);

/// <summary>A legacy content system we are migrating away from (file share, SharePoint).</summary>
public interface IMigrationSource
{
    string Name { get; }
    Task<IReadOnlyList<SourceFile>> EnumerateAsync(CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
}

/// <summary>Thrown by throttling sources (SharePoint/Graph returns 429 + Retry-After).</summary>
public class SourceThrottledException(TimeSpan retryAfter)
    : Exception($"Source throttled; retry after {retryAfter.TotalMilliseconds:F0} ms")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Stand-in for the on-prem file share: plain directory enumeration and reads.</summary>
public class LocalFolderSource(string name, string rootPath) : IMigrationSource
{
    public string Name => name;

    public Task<IReadOnlyList<SourceFile>> EnumerateAsync(CancellationToken ct = default)
    {
        var root = new DirectoryInfo(rootPath);
        IReadOnlyList<SourceFile> files = root.Exists
            ? root.EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(f => new SourceFile(Path.GetRelativePath(rootPath, f.FullName).Replace('\\', '/'), f.Length))
                .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                .ToList()
            : [];
        return Task.FromResult(files);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default) =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(rootPath, relativePath)));
}

/// <summary>
/// Stand-in for SharePoint via Microsoft Graph: pages enumeration in batches, throws
/// 429-style throttling errors the client must back off from, and (like a flaky remote)
/// truncates files whose name contains "corrupt" — those must fail verification, not slip through.
/// </summary>
public class ThrottledGraphSource(string name, string rootPath, int seed = 42) : IMigrationSource
{
    private readonly LocalFolderSource inner = new(name, rootPath);
    private readonly Random random = new(seed);
    private const int PageSize = 100;

    public string Name => name;

    public async Task<IReadOnlyList<SourceFile>> EnumerateAsync(CancellationToken ct = default)
    {
        var all = await inner.EnumerateAsync(ct);
        var result = new List<SourceFile>(all.Count);
        foreach (var page in all.Chunk(PageSize))
        {
            MaybeThrottle(probability: 0.15);
            result.AddRange(page);
        }
        return result;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        MaybeThrottle(probability: 0.05);
        var stream = await inner.OpenReadAsync(relativePath, ct);
        if (relativePath.Contains("corrupt", StringComparison.OrdinalIgnoreCase))
        {
            // Deliver fewer bytes than the reported size — a truncated transfer.
            var truncated = Math.Max(0, stream.Length - Math.Max(1, stream.Length / 10));
            return new TruncatedStream(stream, truncated);
        }
        return stream;
    }

    private void MaybeThrottle(double probability)
    {
        lock (random)
        {
            if (random.NextDouble() < probability)
                throw new SourceThrottledException(TimeSpan.FromMilliseconds(random.Next(100, 400)));
        }
    }

    private sealed class TruncatedStream(Stream inner, long limit) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => limit;
        public override long Position { get => read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowed = (int)Math.Min(count, limit - read);
            if (allowed <= 0) return 0;
            var n = inner.Read(buffer, offset, allowed);
            read += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
    }
}
