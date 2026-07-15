using System.Collections.Concurrent;
using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Observability;

public sealed class TelemetryCollector : ITelemetryCollector
{
    private readonly TimeSpan _activeUserWindow;
    private readonly ConcurrentDictionary<string, AppMetrics> _apps = new(StringComparer.Ordinal);

    public TelemetryCollector(IOptions<ContextMemoryOptions> options)
    {
        var minutes = Math.Max(1, options.Value.ActiveUserWindowMinutes);
        _activeUserWindow = TimeSpan.FromMinutes(minutes);
    }

    public void RecordRequest(
        string appId,
        string userId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens)
    {
        RecordUserActivity(appId, userId);

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Increment(ref metrics.RequestsTotal);
        if (statusCode >= 400)
            Interlocked.Increment(ref metrics.RequestsError);

        Interlocked.Add(ref metrics.TokensPrompt, promptTokens);
        Interlocked.Add(ref metrics.TokensCompletion, completionTokens);

        lock (metrics.LatencyLock)
        {
            metrics.LatencySamples.Add(latencyMs);
            if (metrics.LatencySamples.Count > 1000)
                metrics.LatencySamples.RemoveAt(0);
        }
    }

    public void RecordUserActivity(string appId, string userId)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(userId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        metrics.ActiveUsers[userId] = DateTimeOffset.UtcNow;
        PruneInactiveUsers(metrics);
    }

    public void RecordWikiContext(
        string appId,
        int contextChars,
        int includedPages,
        int totalPages,
        bool truncated)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Exchange(ref metrics.WikiContextChars, Math.Max(0, contextChars));
        Interlocked.Exchange(ref metrics.WikiPagesIncluded, Math.Max(0, includedPages));
        Interlocked.Exchange(ref metrics.WikiPagesTotal, Math.Max(0, totalPages));
        if (truncated)
            Interlocked.Increment(ref metrics.WikiTruncatedTotal);
    }

    public void RecordWikiCompaction(string appId, bool success)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        if (success)
            Interlocked.Increment(ref metrics.WikiCompactionSuccess);
        else
            Interlocked.Increment(ref metrics.WikiCompactionErrors);
    }

    public void RecordWikiMaintainer(string appId, bool success)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        if (success)
            Interlocked.Increment(ref metrics.WikiMaintainerSuccess);
        else
            Interlocked.Increment(ref metrics.WikiMaintainerErrors);
    }

    public void RecordWebSearch(string appId, string provider, string status, int hitCount, double latencyMs)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Exchange(ref metrics.WebSearchLastLatencyMs, latencyMs);

        switch (status.ToLowerInvariant())
        {
            case "hit":
                Interlocked.Increment(ref metrics.WebSearchHitTotal);
                Interlocked.Add(ref metrics.WebSearchHitCount, Math.Max(0, hitCount));
                break;
            case "empty":
                Interlocked.Increment(ref metrics.WebSearchEmptyTotal);
                break;
            default:
                Interlocked.Increment(ref metrics.WebSearchErrorTotal);
                break;
        }

        _ = provider;
    }

    public void RecordWebSearchSkipped(string appId, string reason)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Increment(ref metrics.WebSearchSkippedTotal);
        _ = reason;
    }

    public AppTelemetrySnapshot GetAppSnapshot(string appId)
    {
        if (!_apps.TryGetValue(appId, out var m))
            return new AppTelemetrySnapshot();

        PruneInactiveUsers(m);

        return new AppTelemetrySnapshot
        {
            RequestsTotal = m.RequestsTotal,
            RequestsError = m.RequestsError,
            TokensPrompt = m.TokensPrompt,
            TokensCompletion = m.TokensCompletion,
            AvgLatencyMs = m.LatencySamples.Count > 0 ? m.LatencySamples.Average() : 0,
            ActiveUsers = m.ActiveUsers.Count,
            WikiContextChars = m.WikiContextChars,
            WikiPagesIncluded = m.WikiPagesIncluded,
            WikiPagesTotal = m.WikiPagesTotal,
            WikiTruncatedTotal = m.WikiTruncatedTotal,
            WikiCompactionSuccess = m.WikiCompactionSuccess,
            WikiCompactionErrors = m.WikiCompactionErrors,
            WikiMaintainerSuccess = m.WikiMaintainerSuccess,
            WikiMaintainerErrors = m.WikiMaintainerErrors,
            WebSearchTotal = m.WebSearchHitTotal + m.WebSearchErrorTotal + m.WebSearchEmptyTotal,
            WebSearchHits = m.WebSearchHitCount,
            WebSearchSkippedTotal = m.WebSearchSkippedTotal,
            WebSearchLastLatencyMs = m.WebSearchLastLatencyMs
        };
    }

    public IReadOnlyDictionary<string, AppTelemetrySnapshot> GetAllSnapshots() =>
        _apps.Keys.ToDictionary(k => k, GetAppSnapshot, StringComparer.Ordinal);

    public string ExportPrometheus()
    {
        var sb = new StringBuilder();
        foreach (var (appId, m) in _apps)
        {
            PruneInactiveUsers(m);
            var label = EscapeLabel(appId);
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"success\"}} {m.RequestsTotal - m.RequestsError}");
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"error\"}} {m.RequestsError}");
            sb.AppendLine($"cm_tokens_prompt_total{{appId=\"{label}\"}} {m.TokensPrompt}");
            sb.AppendLine($"cm_tokens_completion_total{{appId=\"{label}\"}} {m.TokensCompletion}");
            sb.AppendLine($"cm_active_users{{appId=\"{label}\"}} {m.ActiveUsers.Count}");

            var p50 = Percentile(m.LatencySamples, 0.5);
            var p95 = Percentile(m.LatencySamples, 0.95);
            var p99 = Percentile(m.LatencySamples, 0.99);
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p50\"}} {p50:F0}");
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p95\"}} {p95:F0}");
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p99\"}} {p99:F0}");
            sb.AppendLine($"cm_wiki_context_chars{{appId=\"{label}\"}} {m.WikiContextChars}");
            sb.AppendLine($"cm_wiki_pages_included{{appId=\"{label}\"}} {m.WikiPagesIncluded}");
            sb.AppendLine($"cm_wiki_pages_total{{appId=\"{label}\"}} {m.WikiPagesTotal}");
            sb.AppendLine($"cm_wiki_truncated_total{{appId=\"{label}\"}} {m.WikiTruncatedTotal}");
            sb.AppendLine($"cm_wiki_compaction_total{{appId=\"{label}\",status=\"success\"}} {m.WikiCompactionSuccess}");
            sb.AppendLine($"cm_wiki_compaction_total{{appId=\"{label}\",status=\"error\"}} {m.WikiCompactionErrors}");
            sb.AppendLine($"cm_wiki_maintainer_total{{appId=\"{label}\",status=\"success\"}} {m.WikiMaintainerSuccess}");
            sb.AppendLine($"cm_wiki_maintainer_total{{appId=\"{label}\",status=\"error\"}} {m.WikiMaintainerErrors}");
            sb.AppendLine($"cm_web_search_total{{appId=\"{label}\",status=\"hit\"}} {m.WebSearchHitTotal}");
            sb.AppendLine($"cm_web_search_total{{appId=\"{label}\",status=\"error\"}} {m.WebSearchErrorTotal}");
            sb.AppendLine($"cm_web_search_total{{appId=\"{label}\",status=\"empty\"}} {m.WebSearchEmptyTotal}");
            sb.AppendLine($"cm_web_search_skipped_total{{appId=\"{label}\"}} {m.WebSearchSkippedTotal}");
            sb.AppendLine($"cm_web_search_hits{{appId=\"{label}\"}} {m.WebSearchHitCount}");
            sb.AppendLine($"cm_web_search_latency_ms{{appId=\"{label}\"}} {m.WebSearchLastLatencyMs:F0}");
        }

        return sb.ToString();
    }

    private void PruneInactiveUsers(AppMetrics metrics)
    {
        var cutoff = DateTimeOffset.UtcNow - _activeUserWindow;
        foreach (var (userId, lastSeen) in metrics.ActiveUsers.ToArray())
        {
            if (lastSeen < cutoff)
                metrics.ActiveUsers.TryRemove(userId, out _);
        }
    }

    private static double Percentile(List<double> samples, double percentile)
    {
        if (samples.Count == 0)
            return 0;

        var sorted = samples.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static string EscapeLabel(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class AppMetrics
    {
        public long RequestsTotal;
        public long RequestsError;
        public long TokensPrompt;
        public long TokensCompletion;
        public int WikiContextChars;
        public int WikiPagesIncluded;
        public int WikiPagesTotal;
        public long WikiTruncatedTotal;
        public long WikiCompactionSuccess;
        public long WikiCompactionErrors;
        public long WikiMaintainerSuccess;
        public long WikiMaintainerErrors;
        public long WebSearchHitTotal;
        public long WebSearchErrorTotal;
        public long WebSearchEmptyTotal;
        public long WebSearchSkippedTotal;
        public long WebSearchHitCount;
        public double WebSearchLastLatencyMs;
        public List<double> LatencySamples { get; } = [];
        public object LatencyLock { get; } = new();
        public ConcurrentDictionary<string, DateTimeOffset> ActiveUsers { get; } = new(StringComparer.Ordinal);
    }
}
