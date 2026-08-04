using ContextMemory.Adapters.OpenAi;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Endpoints;

public static class AdminModelsEndpoint
{
    public static void MapAdminModelsEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/models", ListModelsAsync);
    }

    /// <summary>
    /// Lists models from the host LLM backend (and optionally an app's endpoint override).
    /// Master Key required. Query: <c>?appId=</c> to prefer that app's llmEndpoint/llmApiKey.
    /// </summary>
    private static async Task<IResult> ListModelsAsync(
        string? appId,
        IAppConfigStore appConfigStore,
        IHttpClientFactory httpClientFactory,
        IOptions<ContextMemoryOptions> options,
        CancellationToken cancellationToken)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var byId = new Dictionary<string, OpenAiCompatibleModel>(StringComparer.OrdinalIgnoreCase);
        string? llmEndpoint = null;
        string? llmApiKey = null;

        if (!string.IsNullOrWhiteSpace(appId))
        {
            var cfg = appConfigStore.GetConfig(appId.Trim());
            llmEndpoint = cfg.LlmEndpoint;
            llmApiKey = cfg.LlmApiKey;
            if (!string.IsNullOrWhiteSpace(cfg.LlmModel))
            {
                var active = cfg.LlmModel.Trim();
                byId[active] = new OpenAiCompatibleModel
                {
                    Id = active,
                    Created = created,
                    OwnedBy = "active"
                };
            }
        }

        await LlmBackendModelListing
            .TryAddBackendModelsAsync(
                byId,
                created,
                llmEndpoint,
                llmApiKey,
                options.Value,
                httpClientFactory,
                cancellationToken)
            .ConfigureAwait(false);

        if (byId.Count == 0 && !string.IsNullOrWhiteSpace(options.Value.DefaultLlmModel))
        {
            var fallback = options.Value.DefaultLlmModel.Trim();
            byId[fallback] = new OpenAiCompatibleModel
            {
                Id = fallback,
                Created = created,
                OwnedBy = "host-default"
            };
        }

        var data = byId.Values
            .OrderByDescending(m => string.Equals(m.OwnedBy, "active", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(m => new { id = m.Id, ownedBy = m.OwnedBy })
            .ToList();

        return Results.Json(new { data });
    }
}
