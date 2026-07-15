using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AppsEndpoint
{
    public static void MapAppsEndpoint(this WebApplication app)
    {
        app.MapGet("/apps/{appId}", GetAppAsync);
    }

    private static IResult GetAppAsync(
        HttpContext httpContext,
        string appId,
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        ITelemetryCollector telemetry)
    {
        var headerAppId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;
        if (!string.Equals(headerAppId, appId, StringComparison.Ordinal))
        {
            return Results.Json(
                new { error = "X-App-Id does not match the requested appId." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!appRegistry.TryGetApp(appId, out var profile) || profile is null)
            return Results.NotFound(new { error = "App not found." });

        var config = appConfigStore.GetConfig(appId);
        appRegistry.TryGetRegistration(appId, out var registration);
        var stats = telemetry.GetAppSnapshot(appId);

        return Results.Json(new AppDetailResponse
        {
            AppId = profile.AppId,
            Source = appRegistry.GetAppSource(appId),
            AppName = registration?.AppName,
            Domain = registration?.Domain,
            RegisteredAt = registration?.RegisteredAt,
            DefaultLanguage = profile.DefaultLanguage,
            MaxHistoryMessages = profile.MaxHistoryMessages,
            LlmBackend = config.LlmBackend,
            LlmModel = config.LlmModel,
            StreamingEnabled = config.StreamingEnabled,
            RateLimits = config.RateLimits,
            ActiveUsers = stats.ActiveUsers,
            ConfigEndpoint = $"/apps/{appId}/config"
        });
    }
}

public record AppDetailResponse
{
    public required string AppId { get; init; }
    public string Source { get; init; } = "unknown";
    public string? AppName { get; init; }
    public string? Domain { get; init; }
    public DateTimeOffset? RegisteredAt { get; init; }
    public string DefaultLanguage { get; init; } = "en-US";
    public int MaxHistoryMessages { get; init; }
    public string LlmBackend { get; init; } = "ollama";
    public string LlmModel { get; init; } = string.Empty;
    public bool StreamingEnabled { get; init; }
    public RateLimitConfig RateLimits { get; init; } = new();
    public int ActiveUsers { get; init; }
    public string ConfigEndpoint { get; init; } = string.Empty;
}
