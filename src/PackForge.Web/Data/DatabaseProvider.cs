namespace PackForge.Web.Data;

public enum DbProvider { Postgres, SqlServer }

/// <summary>
/// Provider-specific SQL. PackForge runs on PostgreSQL by default; the SQL Server
/// path (config `Database:Provider=SqlServer`) exists to evidence a SQL Server +
/// stored-procedure stack. The reconciliation report is a real stored procedure
/// (T-SQL) / function (PL/pgSQL), not app-side aggregation.
/// </summary>
public static class DatabaseProvider
{
    public static DbProvider Parse(string? value) =>
        string.Equals(value, "SqlServer", StringComparison.OrdinalIgnoreCase) ? DbProvider.SqlServer : DbProvider.Postgres;

    /// <summary>DDL that (re)creates the reconciliation stored procedure/function.</summary>
    public static string ReconciliationDdl(DbProvider provider) => provider switch
    {
        DbProvider.SqlServer => """
            CREATE OR ALTER PROCEDURE dbo.MigrationReconciliation AS
            BEGIN
                SET NOCOUNT ON;
                SELECT
                    "SourceSystem"                                            AS SourceSystem,
                    COUNT(*)                                                  AS TotalFiles,
                    SUM(CASE WHEN "Status" = 'Verified' THEN 1 ELSE 0 END)    AS VerifiedFiles,
                    SUM(CASE WHEN "Status" = 'Failed'   THEN 1 ELSE 0 END)    AS FailedFiles,
                    SUM(CASE WHEN "Status" = 'Pending'  THEN 1 ELSE 0 END)    AS PendingFiles,
                    SUM("SizeBytes")                                          AS TotalBytes,
                    SUM(CASE WHEN "Status" = 'Verified' THEN "SizeBytes" ELSE 0 END) AS VerifiedBytes,
                    CAST(SUM(CASE WHEN "Status" = 'Verified' THEN 1.0 ELSE 0 END) / NULLIF(COUNT(*), 0) AS float) AS VerificationRate
                FROM "MigrationItems"
                GROUP BY "SourceSystem"
                ORDER BY "SourceSystem";
            END
            """,
        _ => """
            CREATE OR REPLACE FUNCTION migration_reconciliation()
            RETURNS TABLE (
                "SourceSystem" text, "TotalFiles" int, "VerifiedFiles" int, "FailedFiles" int,
                "PendingFiles" int, "TotalBytes" bigint, "VerifiedBytes" bigint, "VerificationRate" double precision
            ) AS $$
                SELECT
                    m."SourceSystem",
                    COUNT(*)::int,
                    SUM(CASE WHEN m."Status" = 'Verified' THEN 1 ELSE 0 END)::int,
                    SUM(CASE WHEN m."Status" = 'Failed'   THEN 1 ELSE 0 END)::int,
                    SUM(CASE WHEN m."Status" = 'Pending'  THEN 1 ELSE 0 END)::int,
                    SUM(m."SizeBytes")::bigint,
                    SUM(CASE WHEN m."Status" = 'Verified' THEN m."SizeBytes" ELSE 0 END)::bigint,
                    (SUM(CASE WHEN m."Status" = 'Verified' THEN 1.0 ELSE 0 END) / NULLIF(COUNT(*), 0))::double precision
                FROM "MigrationItems" m
                GROUP BY m."SourceSystem"
                ORDER BY m."SourceSystem";
            $$ LANGUAGE sql;
            """,
    };

    /// <summary>The statement that invokes the stored procedure/function.</summary>
    public static string ReconciliationCall(DbProvider provider) => provider switch
    {
        DbProvider.SqlServer => "EXEC dbo.MigrationReconciliation",
        _ => "SELECT * FROM migration_reconciliation()",
    };
}
