using System.Diagnostics;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.WebSearch;

public sealed class WebSearchEnricher
{
    private readonly WebSearchFreshnessEvaluator _freshnessEvaluator;
    private readonly IWebSearchProviderResolver _providerResolver;
    private readonly ITelemetryCollector _telemetry;
    private readonly WebSearchOptions _options;
    private readonly ILogger<WebSearchEnricher> _logger;

    public WebSearchEnricher(
        WebSearchFreshnessEvaluator freshnessEvaluator,
        IWebSearchProviderResolver providerResolver,
        ITelemetryCollector telemetry,
        IOptions<ContextMemoryOptions> options,
        ILogger<WebSearchEnricher> logger)
    {
        _freshnessEvaluator = freshnessEvaluator;
        _providerResolver = providerResolver;
        _telemetry = telemetry;
        _options = options.Value.WebSearch;
        _logger = logger;
    }

    public async Task<WebSearchEnrichment> TryEnrichAsync(
        string appId,
        string? userQuery,
        SessionSnapshot snapshot,
        WebSearchConfig config,
        string defaultLanguage = "en-US",
        CancellationToken cancellationToken = default)
    {
        var decision = await _freshnessEvaluator
            .EvaluateAsync(appId, userQuery, snapshot, config, cancellationToken)
            .ConfigureAwait(false);
        if (!decision.ShouldSearch)
        {
            _logger.LogInformation(
                "Web search skipped for {AppId}: {Reason} (source={Source})",
                appId,
                decision.SkipReason ?? "unknown",
                decision.Source ?? "unknown");
            _telemetry.RecordWebSearchSkipped(appId, decision.SkipReason ?? "unknown");
            return WebSearchEnrichment.Skipped(decision.SkipReason ?? "unknown");
        }

        if (!_providerResolver.TryResolve(config.Provider, out var provider) || provider is null)
        {
            _logger.LogWarning("Web search provider '{Provider}' is not registered for {AppId}", config.Provider, appId);
            _telemetry.RecordWebSearchSkipped(appId, "unknown_provider");
            return WebSearchEnrichment.Skipped("unknown_provider");
        }

        if (!HasApiKey(provider.ProviderName))
        {
            _logger.LogWarning("Web search API key missing for provider {Provider}", provider.ProviderName);
            _telemetry.RecordWebSearchSkipped(appId, "no_api_key");
            return WebSearchEnrichment.Skipped("no_api_key");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var maxResults = Math.Clamp(config.MaxResults, 1, 10);
            var result = await provider.SearchAsync(
                new WebSearchRequest(decision.Query!, maxResults),
                cancellationToken).ConfigureAwait(false);

            sw.Stop();
            var maxChars = Math.Clamp(config.MaxContextChars, 256, 12_000);
            var markdown = WebSearchFormatter.ToMarkdown(result, maxChars, userQuery, defaultLanguage);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                _telemetry.RecordWebSearch(appId, provider.ProviderName, "empty", 0, sw.ElapsedMilliseconds);
                return WebSearchEnrichment.Skipped("empty_results");
            }

            _telemetry.RecordWebSearch(appId, provider.ProviderName, "hit", result.Hits.Count, sw.ElapsedMilliseconds);
            return WebSearchEnrichment.FromResult(markdown, result);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Web search failed for {AppId} via {Provider}", appId, provider.ProviderName);
            _telemetry.RecordWebSearch(appId, provider.ProviderName, "error", 0, sw.ElapsedMilliseconds);
            return WebSearchEnrichment.Skipped("error");
        }
    }

    private bool HasApiKey(string providerName) =>
        providerName.Equals("brave", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(_options.BraveApiKey)
            : !string.IsNullOrWhiteSpace(_options.TavilyApiKey);
}

public sealed class WebSearchEnrichment
{
    public bool Used { get; init; }
    public string? PromptMarkdown { get; init; }
    public WebSearchResult? Result { get; init; }
    public string? SkipReason { get; init; }
    public string? Provider { get; init; }

    public static WebSearchEnrichment Skipped(string reason) =>
        new() { SkipReason = reason };

    public static WebSearchEnrichment FromResult(string markdown, WebSearchResult result) =>
        new()
        {
            Used = true,
            PromptMarkdown = markdown,
            Result = result,
            Provider = result.Provider
        };
}
