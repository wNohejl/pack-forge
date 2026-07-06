using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PackForge.Web.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Traces + metrics for the app's own instruments plus ASP.NET Core and HttpClient.
    /// Exports to Azure Monitor (App Insights) when a connection string is configured;
    /// otherwise to the console, so telemetry is provable locally at zero cost.
    /// </summary>
    public static IServiceCollection AddPackForgeObservability(this IServiceCollection services, IConfiguration config)
    {
        var appInsights = config["ApplicationInsights:ConnectionString"]
            ?? config["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Telemetry.ServiceName))
            .WithTracing(t => t
                .AddSource(Telemetry.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(Telemetry.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsights);
        }
        else
        {
            otel.WithTracing(t => t.AddConsoleExporter())
                .WithMetrics(m => m.AddConsoleExporter());
        }

        return services;
    }
}
