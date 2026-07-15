using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Persistence;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Endpoints;

public static class HealthEndpoint
{
    public static void MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", GetHealthAsync);
    }

    private static async Task<IResult> GetHealthAsync(
        HttpContext httpContext,
        ILlmAdapterResolver adapterResolver,
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        var usePostgres = PersistenceProviders.IsPostgres(config.PersistenceProvider);

        var ollamaHealthy = await adapterResolver.Resolve("ollama").IsHealthyAsync().ConfigureAwait(false);
        var appsLoaded = appRegistry.GetAllApps().Count > 0;

        bool profilesReady;
        string? database = null;

        if (usePostgres)
        {
            var pgHealth = httpContext.RequestServices.GetService<IPostgresHealthCheck>();
            var dbUp = pgHealth is not null && await pgHealth.CanConnectAsync().ConfigureAwait(false);
            database = dbUp ? "up" : "down";
            profilesReady = dbUp && appsLoaded;
        }
        else
        {
            profilesReady = Directory.Exists(appConfigStore.ProfilesRoot);
        }

        var healthy = ollamaHealthy && appsLoaded && profilesReady;
        var status = healthy ? "healthy" : "degraded";
        var code = ollamaHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        return Results.Json(new
        {
            status,
            checks = new
            {
                ollama = ollamaHealthy ? "up" : "down",
                database,
                persistence = config.PersistenceProvider,
                appsLoaded,
                profilesReady,
                sessionsPath = Path.Combine(config.DataPath, "sessions"),
                defaultModel = config.DefaultLlmModel
            }
        }, statusCode: code);
    }
}
