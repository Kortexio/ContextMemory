namespace ContextMemory.Core.Contracts;

/// <summary>
/// Fetches web search results for freshness enrichment.
/// </summary>
public interface IWebSearchProvider
{
    string ProviderName { get; }

    Task<WebSearchResult> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default);
}

public sealed record WebSearchRequest(
    string Query,
    int MaxResults = 5,
    string? Region = null,
    DateOnly? PublishedAfter = null);

public sealed record WebSearchResult(
    string Provider,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<WebSearchHit> Hits);

public sealed record WebSearchHit(
    string Title,
    string Url,
    string Snippet,
    DateTimeOffset? PublishedAt);
