using ContextMemory.Core.Session;

namespace ContextMemory.Core.WebSearch;

public interface IWebSearchFreshnessClassifier
{
    Task<WebSearchDecision> ClassifyAsync(
        string appId,
        string userQuery,
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
