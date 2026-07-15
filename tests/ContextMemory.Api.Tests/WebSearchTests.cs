using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Core.WebSearch;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class HeuristicFreshnessDetectorTests
{
    private readonly HeuristicFreshnessDetector _detector = new();

    [Fact]
    public void Evaluate_SearchesWhenFreshnessSignalPresent()
    {
        var decision = _detector.Evaluate(
            "quais as notícias de hoje sobre LGBT nos EUA?",
            EmptySnapshot());

        Assert.True(decision.ShouldSearch);
        Assert.Equal("heuristic", decision.Source);
    }

    [Fact]
    public void Evaluate_SkipsHistoricalQuestions()
    {
        var decision = _detector.Evaluate(
            "como era o movimento negro em 1964?",
            SnapshotWithPages());

        Assert.False(decision.ShouldSearch);
        Assert.Equal("historical", decision.SkipReason);
    }

    [Fact]
    public void Evaluate_ReturnsInconclusiveWhenNoSignals()
    {
        var decision = _detector.Evaluate(
            "explique o movimento negro nos EUA",
            SnapshotWithPages());

        Assert.False(decision.ShouldSearch);
        Assert.Equal("heuristic", decision.SkipReason);
    }

    private static SessionSnapshot EmptySnapshot() =>
        new() { SessionPath = "C:\\tmp\\session" };

    private static SessionSnapshot SnapshotWithPages() =>
        new()
        {
            SessionPath = "C:\\tmp\\session",
            Pages = new Dictionary<string, string>
            {
                ["movimento_negro.md"] = "# Movimento negro"
            }
        };
}

public sealed class WebSearchFreshnessEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_HeuristicMode_FallsBackToLlmWhenInconclusive()
    {
        var llm = new FakeClassifier(WebSearchDecision.Search("US LGBT rights news 2025", "llm"));
        var evaluator = new WebSearchFreshnessEvaluator(llm);

        var decision = await evaluator.EvaluateAsync(
            "demo-dev",
            "explique o movimento negro nos EUA",
            SnapshotWithPages(),
            new WebSearchConfig { Enabled = true, Mode = "heuristic" });

        Assert.True(decision.ShouldSearch);
        Assert.Equal("llm", decision.Source);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_HeuristicMode_DoesNotCallLlmWhenHistorical()
    {
        var llm = new FakeClassifier(WebSearchDecision.Search("x", "llm"));
        var evaluator = new WebSearchFreshnessEvaluator(llm);

        var decision = await evaluator.EvaluateAsync(
            "demo-dev",
            "como era o movimento negro em 1964?",
            SnapshotWithPages(),
            new WebSearchConfig { Enabled = true, Mode = "heuristic" });

        Assert.False(decision.ShouldSearch);
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_LlmMode_AlwaysUsesClassifier()
    {
        var llm = new FakeClassifier(WebSearchDecision.Skip("llm_declined", "llm"));
        var evaluator = new WebSearchFreshnessEvaluator(llm);

        var decision = await evaluator.EvaluateAsync(
            "demo-dev",
            "como era o movimento negro em 1964?",
            SnapshotWithPages(),
            new WebSearchConfig { Enabled = true, Mode = "llm" });

        Assert.False(decision.ShouldSearch);
        Assert.Equal("llm_declined", decision.SkipReason);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsWhenDisabled()
    {
        var llm = new FakeClassifier(WebSearchDecision.Search("x", "llm"));
        var evaluator = new WebSearchFreshnessEvaluator(llm);

        var decision = await evaluator.EvaluateAsync(
            "demo-dev",
            "notícias de hoje",
            SnapshotWithPages(),
            WebSearchConfig.Disabled);

        Assert.False(decision.ShouldSearch);
        Assert.Equal("disabled", decision.SkipReason);
        Assert.Equal(0, llm.CallCount);
    }

    private static SessionSnapshot SnapshotWithPages() =>
        new()
        {
            SessionPath = "C:\\tmp\\session",
            Pages = new Dictionary<string, string> { ["movimento_negro.md"] = "# x" }
        };

    private sealed class FakeClassifier(WebSearchDecision result) : IWebSearchFreshnessClassifier
    {
        public int CallCount { get; private set; }

        public Task<WebSearchDecision> ClassifyAsync(
            string appId,
            string userQuery,
            SessionSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}

public sealed class WebSearchDecisionJsonParserTests
{
    [Fact]
    public void TryParse_AcceptsClassifierJson()
    {
        const string raw = """{"needs_web_search":true,"search_query":"US LGBT news 2025"}""";

        var decision = WebSearchDecisionJsonParser.TryParse(raw);

        Assert.NotNull(decision);
        Assert.True(decision!.ShouldSearch);
        Assert.Equal("US LGBT news 2025", decision.Query);
    }

    [Fact]
    public void TryParse_DeclinedSearchReturnsSkip()
    {
        const string raw = """{"needs_web_search":false,"search_query":null}""";

        var decision = WebSearchDecisionJsonParser.TryParse(raw);

        Assert.NotNull(decision);
        Assert.False(decision!.ShouldSearch);
        Assert.Equal("llm_declined", decision.SkipReason);
    }
}

public sealed class WebSearchFormatterTests
{
    [Fact]
    public void ToMarkdown_IncludesLinksAndProvider()
    {
        var result = new WebSearchResult(
            "tavily",
            new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            [new WebSearchHit("Lei LGBT", "https://example.com/lgbt", "Resumo da lei.", null)]);

        var markdown = WebSearchFormatter.ToMarkdown(result, 3000);

        Assert.Contains("## Web search", markdown, StringComparison.Ordinal);
        Assert.Contains("https://example.com/lgbt", markdown, StringComparison.Ordinal);
        Assert.Contains("tavily", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_WithUserQuery_IncludesAssertiveSourceRules()
    {
        var result = new WebSearchResult(
            "tavily",
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            [new WebSearchHit("Lei", "https://example.com", "Snippet.", null)]);

        var markdown = WebSearchFormatter.ToMarkdown(result, 3000, "novas leis federais este mês");

        Assert.Contains("Question to answer", markdown, StringComparison.Ordinal);
        Assert.Contains("novas leis federais este mês", markdown, StringComparison.Ordinal);
        Assert.Contains("[web]", markdown, StringComparison.Ordinal);
        Assert.Contains("Mandatory rules", markdown, StringComparison.Ordinal);
        Assert.Contains("wiki facts", markdown, StringComparison.Ordinal);
    }
}
