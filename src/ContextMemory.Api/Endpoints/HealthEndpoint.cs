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
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        var usePostgres = PersistenceProviders.IsPostgres(config.PersistenceProvider);

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

        var healthy = appsLoaded && profilesReady;
        var status = healthy ? "healthy" : "unhealthy";
        var code = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        return Results.Json(new
        {
            status,
            checks = new
            {
                database,
                persistence = config.PersistenceProvider,
                appsLoaded,
                profilesReady,
                sessionsPath = Path.Combine(config.DataPath, "sessions"),
                defaultModel = config.DefaultLlmModel,
                harnessHints = new
                {
                    ollamaNumCtxNote =
                        "When llmBackend=ollama and llmOptions.numCtx is set, gateway uses ollama-native (/api/chat) because Ollama /v1 ignores options.num_ctx.",
                    formatJsonNote =
                        "llmOptions.format=json is cleared on agentic turns that send tools (conflicts with tool_calls).",
                    qwenTemplateNote =
                        "Qwen/Bonsai packs with strict Jinja raise_exception need TEMPLATE patch or compatible Modelfile for agentic+tools."
                }
            }
        }, statusCode: code);
    }
}
