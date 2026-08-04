using ContextMemory.Adapters.OpenAi;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Endpoints;

public static class OpenAiModelsEndpoint
{
    public static void MapOpenAiModelsEndpoint(this WebApplication app)
    {
        app.MapGet("/v1/models", HandleAsync).DisableAntiforgery();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IAppConfigStore appConfigStore,
        IHttpClientFactory httpClientFactory,
        IOptions<ContextMemoryOptions> options,
        CancellationToken cancellationToken)
    {
        var appId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var byId = new Dictionary<string, OpenAiCompatibleModel>(StringComparer.OrdinalIgnoreCase);
        string? activeModel = null;

        if (!string.IsNullOrWhiteSpace(appId))
        {
            var cfg = appConfigStore.GetConfig(appId);
            activeModel = string.IsNullOrWhiteSpace(cfg.LlmModel) ? null : cfg.LlmModel.Trim();

            if (!string.IsNullOrWhiteSpace(activeModel))
            {
                byId[activeModel] = new OpenAiCompatibleModel
                {
                    Id = activeModel,
                    Created = created,
                    OwnedBy = "active"
                };
            }

            await LlmBackendModelListing
                .TryAddBackendModelsAsync(
                    byId,
                    created,
                    cfg.LlmEndpoint,
                    cfg.LlmApiKey,
                    options.Value,
                    httpClientFactory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (byId.Count == 0 && !string.IsNullOrWhiteSpace(options.Value.DefaultLlmModel))
        {
            var fallback = options.Value.DefaultLlmModel.Trim();
            byId[fallback] = new OpenAiCompatibleModel
            {
                Id = fallback,
                Created = created,
                OwnedBy = "active"
            };
            activeModel ??= fallback;
        }

        var data = byId.Values
            .OrderByDescending(m => string.Equals(m.OwnedBy, "active", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        httpContext.Response.Headers["X-Context-Memory-Active-Model"] = activeModel ?? string.Empty;

        return Results.Json(new OpenAiCompatibleModelsResponse { Data = data });
    }
}
