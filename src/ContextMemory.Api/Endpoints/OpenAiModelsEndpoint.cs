using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Adapters.OpenAi;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Endpoints;

public static class OpenAiModelsEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

            await TryAddBackendModelsAsync(
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

        // Active model first, then remaining ids alphabetically.
        var data = byId.Values
            .OrderByDescending(m => string.Equals(m.OwnedBy, "active", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        httpContext.Response.Headers["X-Context-Memory-Active-Model"] = activeModel ?? string.Empty;

        return Results.Json(new OpenAiCompatibleModelsResponse { Data = data });
    }

    private static async Task TryAddBackendModelsAsync(
        Dictionary<string, OpenAiCompatibleModel> byId,
        long created,
        string? llmEndpoint,
        string? llmApiKey,
        ContextMemoryOptions hostOptions,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var rawBase = string.IsNullOrWhiteSpace(llmEndpoint)
            ? hostOptions.OllamaEndpoint
            : llmEndpoint;
        if (string.IsNullOrWhiteSpace(rawBase))
            return;

        var openAiBase = NormalizeOpenAiBase(rawBase).TrimEnd('/');
        var rootBase = openAiBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? openAiBase[..^3]
            : openAiBase;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);

            // Prefer OpenAI-compatible /v1/models.
            if (await TryAddFromOpenAiModelsAsync(
                    byId, created, client, $"{openAiBase}/models", llmApiKey ?? hostOptions.OpenAiApiKey, cts.Token)
                .ConfigureAwait(false))
                return;

            // Fallback: Ollama native tags.
            await TryAddFromOllamaTagsAsync(
                    byId, created, client, $"{rootBase.TrimEnd('/')}/api/tags", llmApiKey ?? hostOptions.OpenAiApiKey, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Listing is best-effort; configured active model still returned.
        }
    }

    private static async Task<bool> TryAddFromOpenAiModelsAsync(
        Dictionary<string, OpenAiCompatibleModel> byId,
        long created,
        HttpClient client,
        string url,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return false;

        var payload = await response.Content
            .ReadFromJsonAsync<OpenAiCompatibleModelsResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Data is null || payload.Data.Count == 0)
            return false;

        foreach (var model in payload.Data)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                continue;
            var id = model.Id.Trim();
            if (byId.ContainsKey(id))
                continue;
            byId[id] = new OpenAiCompatibleModel
            {
                Id = id,
                Created = model.Created > 0 ? model.Created : created,
                OwnedBy = string.IsNullOrWhiteSpace(model.OwnedBy) ? "backend" : model.OwnedBy
            };
        }

        return true;
    }

    private static async Task TryAddFromOllamaTagsAsync(
        Dictionary<string, OpenAiCompatibleModel> byId,
        long created,
        HttpClient client,
        string url,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in models.EnumerateArray())
        {
            var id = item.TryGetProperty("name", out var name) ? name.GetString()
                : item.TryGetProperty("model", out var model) ? model.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            id = id.Trim();
            if (byId.ContainsKey(id))
                continue;
            byId[id] = new OpenAiCompatibleModel
            {
                Id = id,
                Created = created,
                OwnedBy = "backend"
            };
        }
    }

    private static string NormalizeOpenAiBase(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return $"{trimmed}/v1";
    }
}
