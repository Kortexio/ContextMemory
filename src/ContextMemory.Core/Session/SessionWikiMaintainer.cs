using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Session;

public sealed class SessionWikiMaintainer
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ITelemetryCollector _telemetry;
    private readonly ILogger<SessionWikiMaintainer> _logger;

    public SessionWikiMaintainer(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        ITelemetryCollector telemetry,
        ILogger<SessionWikiMaintainer> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<SessionWikiUpdate?> UpdateAsync(
        string appId,
        SessionSnapshot snapshot,
        string userMessage,
        string assistantMessage,
        int maintainerBudgetChars,
        string? webSearchSection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage) && string.IsNullOrWhiteSpace(assistantMessage))
            return null;

        var config = _appConfigStore.GetConfig(appId);
        var lang = config.DefaultLanguage;
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var pagesCompiled = SessionWikiCompiler.Compile(snapshot, userMessage, maintainerBudgetChars, includeIndex: false);
        var pagesText = pagesCompiled.IncludedPages > 0
            ? pagesCompiled.Content
            : snapshot.Pages.Count == 0
                ? LlmPrompts.NoneLabel(lang)
                : LlmPrompts.InsufficientBudgetPages(
                    snapshot.Pages.Count,
                    snapshot.Pages.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
                    lang);

        var webSection = string.IsNullOrWhiteSpace(webSearchSection)
            ? string.Empty
            : "\n\n" + webSearchSection.Trim();

        var prompt = LlmPrompts.WikiMaintainer(lang)
            .Replace("{schema}", snapshot.SchemaMd)
            .Replace("{index}", string.IsNullOrWhiteSpace(snapshot.IndexMd)
                ? LlmPrompts.EmptyLabel(lang)
                : snapshot.IndexMd)
            .Replace("{pages}", pagesText)
            .Replace("{userMessage}", userMessage)
            .Replace("{assistantMessage}", assistantMessage)
            .Replace("{webSearchSection}", webSection)
            .Replace("{language}", lang);

        try
        {
            var response = await adapter.GenerateAsync(new OllamaGenerateRequest
            {
                Model = config.LlmModel,
                Prompt = prompt,
                Stream = false,
                Format = "json"
            }, cancellationToken).ConfigureAwait(false);

            var raw = OllamaLlmText.GetGenerateText(response);
            var update = SessionWikiJsonParser.TryParseUpdate(raw);
            if (update is not null)
            {
                _telemetry.RecordWikiMaintainer(appId, success: true);
                return update;
            }

            _logger.LogWarning(
                "Session wiki maintainer returned unparseable JSON for {AppId}. Raw length={Length}",
                appId,
                raw.Length);

            _telemetry.RecordWikiMaintainer(appId, success: false);
            return SessionWikiJsonParser.CreateFallbackLogEntry(
                LlmPrompts.WikiMaintainerInvalidJson(Truncate(userMessage, 40), lang),
                lang);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session wiki update failed for {AppId}", appId);
            _telemetry.RecordWikiMaintainer(appId, success: false);
            return SessionWikiJsonParser.CreateFallbackLogEntry(
                LlmPrompts.WikiMaintainerCompileFailed(lang),
                lang);
        }
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";
}
