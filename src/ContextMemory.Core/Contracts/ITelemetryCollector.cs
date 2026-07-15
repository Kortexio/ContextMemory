namespace ContextMemory.Core.Contracts;

/// <summary>
/// Collects request, wiki, web search, and agentic metrics.
/// </summary>
public interface ITelemetryCollector
{
    void RecordRequest(
        string appId,
        string userId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens);

    void RecordUserActivity(string appId, string userId);

    void RecordWikiContext(
        string appId,
        int contextChars,
        int includedPages,
        int totalPages,
        bool truncated);

    void RecordWikiCompaction(string appId, bool success);

    void RecordWikiMaintainer(string appId, bool success);

    void RecordWebSearch(string appId, string provider, string status, int hitCount, double latencyMs);

    void RecordWebSearchSkipped(string appId, string reason);

    string ExportPrometheus();
    AppTelemetrySnapshot GetAppSnapshot(string appId);
    IReadOnlyDictionary<string, AppTelemetrySnapshot> GetAllSnapshots();
}

public sealed class AppTelemetrySnapshot
{
    public long RequestsTotal { get; init; }
    public long RequestsError { get; init; }
    public long TokensPrompt { get; init; }
    public long TokensCompletion { get; init; }
    public double AvgLatencyMs { get; init; }
    public int ActiveUsers { get; init; }
    public int WikiContextChars { get; init; }
    public int WikiPagesIncluded { get; init; }
    public int WikiPagesTotal { get; init; }
    public long WikiTruncatedTotal { get; init; }
    public long WikiCompactionSuccess { get; init; }
    public long WikiCompactionErrors { get; init; }
    public long WikiMaintainerSuccess { get; init; }
    public long WikiMaintainerErrors { get; init; }
    public long WebSearchTotal { get; init; }
    public long WebSearchHits { get; init; }
    public long WebSearchSkippedTotal { get; init; }
    public double WebSearchLastLatencyMs { get; init; }
}
