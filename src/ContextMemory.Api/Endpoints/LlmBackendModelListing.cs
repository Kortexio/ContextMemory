using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Adapters.OpenAi;
using ContextMemory.Core.Configuration;

namespace ContextMemory.Api.Endpoints;

/// <summary>Best-effort model listing from an OpenAI-compatible or Ollama backend.</summary>
internal static class LlmBackendModelListing
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task TryAddBackendModelsAsync(
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

            if (await TryAddFromOpenAiModelsAsync(
                    byId, created, client, $"{openAiBase}/models", llmApiKey ?? hostOptions.OpenAiApiKey, cts.Token)
                .ConfigureAwait(false))
                return;

            await TryAddFromOllamaTagsAsync(
                    byId, created, client, $"{rootBase.TrimEnd('/')}/api/tags", llmApiKey ?? hostOptions.OpenAiApiKey, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Listing is best-effort.
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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

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
