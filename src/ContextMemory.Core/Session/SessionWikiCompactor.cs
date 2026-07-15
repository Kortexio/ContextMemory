using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Session;

public sealed class SessionWikiCompactor
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ITelemetryCollector _telemetry;
    private readonly ILogger<SessionWikiCompactor> _logger;

    public SessionWikiCompactor(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        ITelemetryCollector telemetry,
        ILogger<SessionWikiCompactor> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task CompactAsync(
        string appId,
        string userId,
        string sessionId,
        ISessionStore sessionStore,
        int maintainerBudgetChars,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await sessionStore.LoadAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
            var config = _appConfigStore.GetConfig(appId);
            var lang = config.DefaultLanguage;
            var compiled = SessionWikiCompiler.Compile(snapshot, userQuery: null, maintainerBudgetChars);

            var pagesText = compiled.IncludedPages == 0
                ? LlmPrompts.NoPagesInBudget(
                    snapshot.Pages.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
                    lang)
                : compiled.Content;

            var prompt = LlmPrompts.WikiCompactor(lang)
                .Replace("{index}", string.IsNullOrWhiteSpace(snapshot.IndexMd)
                    ? LlmPrompts.EmptyLabel(lang)
                    : snapshot.IndexMd)
                .Replace("{pages}", pagesText)
                .Replace("{language}", lang);

            var adapter = _adapterResolver.Resolve(config.LlmBackend);
            var response = await adapter.GenerateAsync(new OllamaGenerateRequest
            {
                Model = config.LlmModel,
                Prompt = prompt,
                Stream = false,
                Format = "json"
            }, cancellationToken).ConfigureAwait(false);

            var raw = OllamaLlmText.GetGenerateText(response);
            var update = SessionWikiJsonParser.TryParseCompaction(raw);
            if (update is null)
            {
                _logger.LogWarning(
                    "Session wiki compaction returned unparseable JSON for {AppId} session {SessionId}",
                    appId,
                    sessionId);
                _telemetry.RecordWikiCompaction(appId, success: false);
                return;
            }

            await sessionStore.ApplyWikiUpdateAsync(appId, userId, sessionId, update, cancellationToken).ConfigureAwait(false);
            _telemetry.RecordWikiCompaction(appId, success: true);
            _logger.LogInformation(
                "Session wiki compacted for {AppId} session {SessionId}",
                appId,
                sessionId);
        }
        catch (Exception ex)
        {
            _telemetry.RecordWikiCompaction(appId, success: false);
            _logger.LogWarning(ex, "Session wiki compaction failed for {AppId}", appId);
        }
    }
}
