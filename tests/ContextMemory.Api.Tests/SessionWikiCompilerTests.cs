using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SessionWikiCompilerTests
{
    [Fact]
    public void Compile_EmptyWiki_ReturnsPlaceholder()
    {
        var snapshot = new SessionSnapshot { SessionPath = "/tmp" };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: null, budgetChars: 12_000);

        Assert.Equal("(session wiki still empty)", result.Content);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Compile_PlaceholderIndexWithoutContent_ReturnsPlaceholder()
    {
        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            IndexMd = SessionDefaults.EmptyIndex
        };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: null, budgetChars: 12_000);

        Assert.Equal("(session wiki still empty)", result.Content);
    }

    [Fact]
    public void Compile_IncludesRecentLogEntries()
    {
        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            IndexMd = SessionDefaults.EmptyIndex,
            LogMd = """
                # Session log

                _Turn chronology._

                ## [2026-05-27 10:00] turn | asked about API
                ## [2026-05-27 10:05] turn | explained authentication
                """
        };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: null, budgetChars: 12_000);

        Assert.Contains("Recent chronology", result.Content);
        Assert.Contains("authentication", result.Content);
        Assert.DoesNotContain("No pages yet", result.Content);
    }

    [Fact]
    public void Compile_RespectsBudgetAndTruncates()
    {
        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            IndexMd = "- [alpha](pages/alpha.md)\n- [beta](pages/beta.md)",
            Pages = new Dictionary<string, string>
            {
                ["alpha"] = new string('a', 500),
                ["beta"] = new string('b', 500)
            }
        };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: "alpha topic", budgetChars: 300);

        Assert.Equal(2, result.TotalPages);
        Assert.Contains("alpha", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.IncludedPages < result.TotalPages || result.Truncated);
    }

    [Fact]
    public void Compile_PrefersQueryRelevantPage()
    {
        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            IndexMd = "- [billing](pages/billing.md)\n- [weather](pages/weather.md)",
            Pages = new Dictionary<string, string>
            {
                ["billing"] = "Factura mensal e IBAN.",
                ["weather"] = "Lisboa está ensolarada."
            }
        };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: "factura IBAN", budgetChars: 200);

        Assert.Contains("billing", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Factura", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_WithoutIndex_UsesBudgetForPagesOnly()
    {
        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            IndexMd = "- [big-index](pages/x.md)",
            Pages = new Dictionary<string, string>
            {
                ["small"] = "conteúdo curto"
            }
        };

        var result = SessionWikiCompiler.Compile(snapshot, userQuery: null, budgetChars: 500, includeIndex: false);

        Assert.Contains("small", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Index", result.Content);
    }

    [Fact]
    public void ShouldCompact_RequiresMinPagesAndThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-wiki-test-" + Guid.NewGuid().ToString("N"));
        var pagesDir = Path.Combine(dir, "pages");
        Directory.CreateDirectory(pagesDir);

        try
        {
            for (var i = 0; i < 10; i++)
                File.WriteAllText(Path.Combine(pagesDir, $"p{i}.md"), new string('x', 1024));

            var snapshot = new SessionSnapshot { SessionPath = dir };
            var config = new AppRuntimeConfig
            {
                AppId = "test",
                WikiCompactionThresholdBytes = 4_096,
                WikiCompactionMinPages = 8
            };
            var defaults = new ContextMemoryOptions
            {
                WikiCompactionThresholdBytes = 524_288,
                WikiCompactionMinPages = 8
            };

            Assert.True(SessionWikiSettings.ShouldCompact(snapshot, config, defaults));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
