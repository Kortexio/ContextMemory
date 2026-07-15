using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Endpoints;

public static class MetricsEndpoint
{
    public static void MapMetricsEndpoint(this WebApplication app)
    {
        app.MapGet("/metrics", (ITelemetryCollector telemetry) =>
        {
            var body = telemetry.ExportPrometheus();
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });
    }
}
