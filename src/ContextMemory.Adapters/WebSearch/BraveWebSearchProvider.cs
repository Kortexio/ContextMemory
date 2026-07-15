using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters.WebSearch;

public sealed class BraveWebSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly WebSearchOptions _options;

    public BraveWebSearchProvider(HttpClient http, IOptions<ContextMemoryOptions> options)
    {
        _http = http;
        _options = options.Value.WebSearch;
    }

    public string ProviderName => "brave";

    public async Task<WebSearchResult> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        var url =
            $"res/v1/web/search?q={Uri.EscapeDataString(request.Query)}&count={request.MaxResults}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Add("X-Subscription-Token", _options.BraveApiKey);

        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<BraveResponse>(cancellationToken).ConfigureAwait(false)
                   ?? new BraveResponse();

        var hits = body.Web?.Results?
            .Select(r => new WebSearchHit(
                r.Title ?? string.Empty,
                r.Url ?? string.Empty,
                r.Description ?? string.Empty,
                r.PageAge is { } age && DateTimeOffset.TryParse(age, out var published) ? published : null))
            .Where(h => !string.IsNullOrWhiteSpace(h.Url))
            .Take(request.MaxResults)
            .ToList() ?? [];

        return new WebSearchResult(ProviderName, DateTimeOffset.UtcNow, hits);
    }

    private sealed class BraveResponse
    {
        [JsonPropertyName("web")]
        public BraveWeb? Web { get; set; }
    }

    private sealed class BraveWeb
    {
        [JsonPropertyName("results")]
        public List<BraveHit>? Results { get; set; }
    }

    private sealed class BraveHit
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("page_age")]
        public string? PageAge { get; set; }
    }
}
