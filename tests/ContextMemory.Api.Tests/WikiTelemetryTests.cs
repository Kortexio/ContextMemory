using ContextMemory.Core.Configuration;
using ContextMemory.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class WikiTelemetryTests
{
    [Fact]
    public void RecordWikiContext_UpdatesSnapshotAndPrometheusExport()
    {
        var collector = new TelemetryCollector(Options.Create(new ContextMemoryOptions()));

        collector.RecordWikiContext("demo-dev", contextChars: 4500, includedPages: 3, totalPages: 10, truncated: true);
        collector.RecordWikiContext("demo-dev", contextChars: 8000, includedPages: 5, totalPages: 12, truncated: false);
        collector.RecordWikiCompaction("demo-dev", success: true);
        collector.RecordWikiCompaction("demo-dev", success: false);
        collector.RecordWikiMaintainer("demo-dev", success: true);
        collector.RecordWikiMaintainer("demo-dev", success: false);

        var snapshot = collector.GetAppSnapshot("demo-dev");
        Assert.Equal(8000, snapshot.WikiContextChars);
        Assert.Equal(5, snapshot.WikiPagesIncluded);
        Assert.Equal(12, snapshot.WikiPagesTotal);
        Assert.Equal(1, snapshot.WikiTruncatedTotal);
        Assert.Equal(1, snapshot.WikiCompactionSuccess);
        Assert.Equal(1, snapshot.WikiCompactionErrors);
        Assert.Equal(1, snapshot.WikiMaintainerSuccess);
        Assert.Equal(1, snapshot.WikiMaintainerErrors);

        var prometheus = collector.ExportPrometheus();
        Assert.Contains("cm_wiki_context_chars{appId=\"demo-dev\"} 8000", prometheus);
        Assert.Contains("cm_wiki_pages_included{appId=\"demo-dev\"} 5", prometheus);
        Assert.Contains("cm_wiki_pages_total{appId=\"demo-dev\"} 12", prometheus);
        Assert.Contains("cm_wiki_truncated_total{appId=\"demo-dev\"} 1", prometheus);
        Assert.Contains("cm_wiki_compaction_total{appId=\"demo-dev\",status=\"success\"} 1", prometheus);
        Assert.Contains("cm_wiki_compaction_total{appId=\"demo-dev\",status=\"error\"} 1", prometheus);
        Assert.Contains("cm_wiki_maintainer_total{appId=\"demo-dev\",status=\"success\"} 1", prometheus);
        Assert.Contains("cm_wiki_maintainer_total{appId=\"demo-dev\",status=\"error\"} 1", prometheus);
    }
}
