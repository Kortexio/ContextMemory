using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.WebSearch;

public sealed class WebSearchFreshnessEvaluator
{
    private readonly HeuristicFreshnessDetector _heuristic = new();
    private readonly IWebSearchFreshnessClassifier _llm;

    public WebSearchFreshnessEvaluator(IWebSearchFreshnessClassifier llm)
    {
        _llm = llm;
    }

    public async Task<WebSearchDecision> EvaluateAsync(
        string appId,
        string? userQuery,
        SessionSnapshot snapshot,
        WebSearchConfig config,
        CancellationToken cancellationToken = default)
    {
        if (!config.IsActive)
            return WebSearchDecision.Skip("disabled", "heuristic");

        if (string.IsNullOrWhiteSpace(userQuery))
            return WebSearchDecision.Skip("empty_query", "heuristic");

        var query = userQuery.Trim();

        if (string.Equals(config.Mode, "always", StringComparison.OrdinalIgnoreCase))
            return WebSearchDecision.Search(query, "always");

        if (string.Equals(config.Mode, "llm", StringComparison.OrdinalIgnoreCase))
            return await _llm.ClassifyAsync(appId, query, snapshot, cancellationToken).ConfigureAwait(false);

        var heuristic = _heuristic.Evaluate(query, snapshot);
        if (heuristic.ShouldSearch)
            return heuristic with { Source = "heuristic" };

        if (heuristic.SkipReason is "historical" or "empty_query")
            return heuristic with { Source = "heuristic" };

        return await _llm.ClassifyAsync(appId, query, snapshot, cancellationToken).ConfigureAwait(false);
    }
}
