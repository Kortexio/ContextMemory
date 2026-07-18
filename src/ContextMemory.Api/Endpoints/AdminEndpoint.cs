using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AdminEndpoint
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin", () => Results.Content(GetDashboardHtml(), "text/html"));
        app.MapGet("/admin/apps", ListApps);
        app.MapGet("/admin/apps/{appId}/stats", GetStats);
        app.MapPatch("/admin/apps/{appId}/config", PatchConfig);
        app.MapGet("/admin/apps/{appId}/credentials", GetCredentials);
        app.MapPost("/admin/apps/{appId}/rotate-api-key", RotateApiKey).DisableAntiforgery();
        app.MapDelete("/admin/apps/{appId}", DeactivateApp);
    }

    private static IResult ListApps(IAppRegistry registry, ITelemetryCollector telemetry) =>
        Results.Json(registry.GetAllApps().Select(a => new
        {
            a.AppId,
            a.IsActive,
            source = registry.GetAppSource(a.AppId),
            stats = telemetry.GetAppSnapshot(a.AppId)
        }));

    private static IResult GetStats(string appId, ITelemetryCollector telemetry)
    {
        var snapshot = telemetry.GetAppSnapshot(appId);
        return Results.Json(new { appId, activeUsers = snapshot.ActiveUsers, telemetry = snapshot });
    }

    private static async Task<IResult> PatchConfig(
        string appId,
        AppConfigPatchRequest patch,
        IAppConfigStore configStore,
        CancellationToken cancellationToken)
    {
        var updated = await configStore.UpdateAsync(appId, patch, cancellationToken).ConfigureAwait(false);
        return Results.Json(updated);
    }

    private static IResult GetCredentials(string appId, IAppRegistry registry)
    {
        if (!registry.TryGetCredentials(appId, out var credentials) || credentials is null)
            return Results.NotFound(new { error = "App not found." });

        return Results.Json(credentials);
    }

    private static IResult DeactivateApp(string appId, IAppRegistry registry)
    {
        if (!registry.TryDeactivateApp(appId))
            return Results.NotFound(new { error = "App not found." });

        return Results.NoContent();
    }

    private static IResult RotateApiKey(string appId, IAppRegistry registry)
    {
        if (!registry.TryRotateApiKey(appId, out var credentials) || credentials is null)
            return Results.NotFound(new { error = "App not found." });

        return Results.Json(credentials);
    }

    private static string GetDashboardHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>ContextMemory Admin</title>
          <style>
            body { font-family: system-ui, sans-serif; margin: 2rem; background: #0f172a; color: #e2e8f0; }
            h1 { color: #38bdf8; }
            a { color: #7dd3fc; }
            code, pre { background: #1e293b; padding: 0.15rem 0.4rem; border-radius: 6px; }
            pre { padding: 1rem; overflow: auto; }
          </style>
        </head>
        <body>
          <h1>ContextMemory Admin</h1>
          <p>For the full console (apps, config, <strong>Chat Lab</strong>), run the Admin UI host:</p>
          <pre>cd src/ContextMemory.Admin.Web<br />dotnet run</pre>
          <p>Then open <a href="http://localhost:5200">http://localhost:5200</a> — set API URL <code>http://localhost:5100</code> and your Master Key in Settings.</p>
          <p>Admin HTTP APIs (Master Key required): <code>GET /admin/apps</code>, <code>PATCH /admin/apps/{id}/config</code>, …</p>
          <p>Chat headers: <code>X-App-Id</code>, <code>X-User-Id</code>, <code>X-Session-Id</code> (optional), <code>Authorization: Bearer API_KEY</code></p>
        </body>
        </html>
        """;
}
