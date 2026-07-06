namespace PackForge.Core;

public enum UploadStatus
{
    Pending,
    Ready,
    Failed,
}

public class Upload
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string BlobName { get; set; } = string.Empty;
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
