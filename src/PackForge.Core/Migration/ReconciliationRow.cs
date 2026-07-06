namespace PackForge.Core.Migration;

/// <summary>
/// One row of the migration reconciliation report — produced by a database stored
/// procedure (SQL Server) / function (PostgreSQL), not by app-side aggregation.
/// Keyless: it maps to a result set, not a table.
/// </summary>
public class ReconciliationRow
{
    public string SourceSystem { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int VerifiedFiles { get; set; }
    public int FailedFiles { get; set; }
    public int PendingFiles { get; set; }
    public long TotalBytes { get; set; }
    public long VerifiedBytes { get; set; }
    public double VerificationRate { get; set; }
}
