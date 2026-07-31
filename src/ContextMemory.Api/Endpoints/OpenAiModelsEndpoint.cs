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

    private static IResult HandleAsync(
        HttpContext httpContext,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        var appId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var models = new List<OpenAiCompatibleModel>();

        if (!string.IsNullOrWhiteSpace(appId))
        {
            var cfg = appConfigStore.GetConfig(appId);
            if (!string.IsNullOrWhiteSpace(cfg.LlmModel))
            {
                models.Add(new OpenAiCompatibleModel
                {
                    Id = cfg.LlmModel,
                    Created = created,
                    OwnedBy = appId
                });
            }
        }

        if (models.Count == 0 && !string.IsNullOrWhiteSpace(options.Value.DefaultLlmModel))
        {
            models.Add(new OpenAiCompatibleModel
            {
                Id = options.Value.DefaultLlmModel,
                Created = created
            });
        }

        return Results.Json(new OpenAiCompatibleModelsResponse { Data = models });
    }
}
