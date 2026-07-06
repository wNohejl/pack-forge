namespace PackForge.Core;

public enum BuildStatus
{
    Queued,
    Building,
    Ready,
    Failed,
}

public class PackageBuild
{
    public Guid Id { get; set; }
    public Guid UploadId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ModelSha256 { get; set; } = string.Empty;
    public string? PackageSha256 { get; set; }
    public string? BlobName { get; set; }
    public BuildStatus Status { get; set; } = BuildStatus.Queued;
    public string? Error { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
