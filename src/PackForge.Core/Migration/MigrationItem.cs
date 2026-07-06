namespace PackForge.Core.Migration;

public enum MigrationStatus
{
    Pending,
    Copied,
    Verified,
    Failed,
}

public class MigrationItem
{
    public Guid Id { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? SourceSha256 { get; set; }
    public string? BlobSha256 { get; set; }
    public string? BlobName { get; set; }
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;
    public string? Error { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VerifiedAt { get; set; }
}
