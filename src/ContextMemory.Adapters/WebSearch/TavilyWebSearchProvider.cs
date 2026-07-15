using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters.WebSearch;

public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly WebSearchOptions _options;

    public TavilyWebSearchProvider(HttpClient http, IOptions<ContextMemoryOptions> options)
    {
        _http = http;
        _options = options.Value.WebSearch;
    }

    public string ProviderName => "tavily";

    public async Task<WebSearchResult> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new TavilyRequest
        {
            ApiKey = _options.TavilyApiKey,
            Query = request.Query,
            MaxResults = request.MaxResults,
            SearchDepth = "basic"
        };

        using var response = await _http.PostAsJsonAsync("search", payload, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TavilyResponse>(cancellationToken).ConfigureAwait(false)
                   ?? new TavilyResponse();

        var hits = body.Results
            .Select(r => new WebSearchHit(
                r.Title ?? string.Empty,
                r.Url ?? string.Empty,
                r.Content ?? string.Empty,
                null))
            .Where(h => !string.IsNullOrWhiteSpace(h.Url))
            .Take(request.MaxResults)
            .ToList();

        return new WebSearchResult(ProviderName, DateTimeOffset.UtcNow, hits);
    }

    private sealed class TavilyRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; }

        [JsonPropertyName("search_depth")]
        public string SearchDepth { get; set; } = "basic";
    }

    private sealed class TavilyResponse
    {
        [JsonPropertyName("results")]
        public List<TavilyHit> Results { get; set; } = [];
    }

    private sealed class TavilyHit
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
