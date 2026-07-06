using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PackForge.Web.Observability;

/// <summary>
/// App-owned tracing/metric instruments. Registered with OpenTelemetry so the
/// upload → build → migrate paths show up as spans and counters in App Insights.
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "packforge";

    public static readonly ActivitySource Source = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> PackagesBuilt =
        Meter.CreateCounter<long>("packforge.packages.built", unit: "{package}", description: "Deployment packages built");
    public static readonly Histogram<double> BuildDurationMs =
        Meter.CreateHistogram<double>("packforge.build.duration", unit: "ms", description: "Package build duration");
    public static readonly Counter<long> MigrationFilesVerified =
        Meter.CreateCounter<long>("packforge.migration.verified", unit: "{file}", description: "Files verified during migration");
    public static readonly Counter<long> MigrationFilesFailed =
        Meter.CreateCounter<long>("packforge.migration.failed", unit: "{file}", description: "Files that failed migration verification");
}
