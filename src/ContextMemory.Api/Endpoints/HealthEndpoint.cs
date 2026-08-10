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

        // Cap LLM probe so Docker healthchecks (short curl timeout) do not hang on a slow backend.
        // Default backend is OpenAI-compatible (/v1/models) via OllamaEndpoint or OpenAiEndpoint.
        bool llmHealthy;
        using (var llmCts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
        {
            llmCts.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                llmHealthy = await adapterResolver
                    .Resolve("ollama")
                    .IsHealthyAsync(llmCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                llmHealthy = false;
            }
        }

        var ollamaHealthy = llmHealthy;

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

        // Process is live when persistence/apps are ready. Ollama may be degraded without failing Docker health.
        var healthy = appsLoaded && profilesReady;
        var status = healthy
            ? (ollamaHealthy ? "healthy" : "degraded")
            : "unhealthy";
        var code = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

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
