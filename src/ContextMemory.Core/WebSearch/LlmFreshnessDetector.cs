using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.WebSearch;

public sealed class LlmFreshnessDetector : IWebSearchFreshnessClassifier
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly WebSearchOptions _options;
    private readonly ILogger<LlmFreshnessDetector> _logger;

    public LlmFreshnessDetector(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options,
        ILogger<LlmFreshnessDetector> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _options = options.Value.WebSearch;
        _logger = logger;
    }

    public Task<WebSearchDecision> ClassifyAsync(
        string appId,
        string userQuery,
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(appId, userQuery, snapshot, cancellationToken);

    public async Task<WebSearchDecision> EvaluateAsync(
        string appId,
        string userQuery,
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);
        var lang = config.DefaultLanguage;
        var wikiPages = snapshot.Pages.Count == 0
            ? LlmPrompts.NoneLabel(lang)
            : string.Join(", ", snapshot.Pages.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

        var prompt = LlmPrompts.FreshnessClassifier(lang)
            .Replace("{wikiPages}", wikiPages)
            .Replace("{userQuery}", userQuery.Trim());

        var timeoutSeconds = Math.Clamp(_options.ClassifierTimeoutSeconds, 3, 60);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var response = await adapter.GenerateAsync(
                new OllamaGenerateRequest
                {
                    Model = config.LlmModel,
                    Prompt = prompt,
                    Stream = false,
                    Format = "json"
                },
                timeoutCts.Token).ConfigureAwait(false);

            var raw = OllamaLlmText.GetGenerateText(response);
            var decision = WebSearchDecisionJsonParser.TryParse(raw);
            if (decision is not null)
                return decision;

            _logger.LogWarning(
                "LLM web-search classifier returned unparseable JSON for {AppId}. Raw length={Length}",
                appId,
                raw.Length);
            return WebSearchDecision.Skip("llm_unparseable", "llm");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("LLM web-search classifier timed out for {AppId}", appId);
            return WebSearchDecision.Skip("llm_timeout", "llm");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM web-search classifier failed for {AppId}", appId);
            return WebSearchDecision.Skip("llm_error", "llm");
        }
    }
}
