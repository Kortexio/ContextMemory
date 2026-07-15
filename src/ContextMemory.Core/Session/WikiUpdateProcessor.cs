using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.WebSearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Session;

public sealed class WikiUpdateProcessor
{
    private readonly ISessionStore _sessionStore;
    private readonly SessionWikiMaintainer _wikiMaintainer;
    private readonly SessionWikiCompactor _wikiCompactor;
    private readonly ITelemetryCollector _telemetry;
    private readonly ILogger<WikiUpdateProcessor> _logger;
    private readonly ContextMemoryOptions _options;

    public WikiUpdateProcessor(
        ISessionStore sessionStore,
        SessionWikiMaintainer wikiMaintainer,
        SessionWikiCompactor wikiCompactor,
        ITelemetryCollector telemetry,
        ILogger<WikiUpdateProcessor> logger,
        IOptions<ContextMemoryOptions> options)
    {
        _sessionStore = sessionStore;
        _wikiMaintainer = wikiMaintainer;
        _wikiCompactor = wikiCompactor;
        _telemetry = telemetry;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ProcessAsync(WikiUpdateJob job, CancellationToken cancellationToken)
    {
        try
        {
            var config = job.RuntimeConfig;
            var snapshot = await _sessionStore
                .LoadAsync(job.AppId, job.UserId, job.SessionId, cancellationToken)
                .ConfigureAwait(false);

            if (job.WebEnrichment?.Used == true
                && config.WebSearch.LogWebSearch
                && job.WebEnrichment.Result is not null)
            {
                var logEntry =
                    $"## [{DateTime.UtcNow:yyyy-MM-dd HH:mm}] web search | "
                    + $"{job.WebEnrichment.Result.Hits.Count} hits | {job.WebEnrichment.Result.Provider}";
                await _sessionStore.ApplyWikiUpdateAsync(
                        job.AppId,
                        job.UserId,
                        job.SessionId,
                        new SessionWikiUpdate { LogEntry = logEntry },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var maintainerBudget = SessionWikiSettings.ResolveMaintainerWikiBudgetChars(config, _options);
            string? webSection = null;
            if (job.WebEnrichment?.Used == true
                && config.WebSearch.PersistToWiki
                && job.WebEnrichment.Result is not null)
            {
                webSection = WebSearchFormatter.ToMaintainerSection(job.WebEnrichment.Result, config.DefaultLanguage);
            }

            var update = await _wikiMaintainer
                .UpdateAsync(
                    job.AppId,
                    snapshot,
                    job.UserMessage,
                    job.AssistantMessage,
                    maintainerBudget,
                    webSection,
                    cancellationToken)
                .ConfigureAwait(false);

            if (update is null)
                return;

            await _sessionStore
                .ApplyWikiUpdateAsync(job.AppId, job.UserId, job.SessionId, update, cancellationToken)
                .ConfigureAwait(false);

            snapshot = await _sessionStore
                .LoadAsync(job.AppId, job.UserId, job.SessionId, cancellationToken)
                .ConfigureAwait(false);

            if (!SessionWikiSettings.ShouldCompact(snapshot, config, _options))
                return;

            await _wikiCompactor
                .CompactAsync(job.AppId, job.UserId, job.SessionId, _sessionStore, maintainerBudget, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Wiki update cancelled for {AppId} session {SessionId}",
                job.AppId,
                job.SessionId);
        }
        catch (Exception ex)
        {
            _telemetry.RecordWikiMaintainer(job.AppId, success: false);
            _logger.LogWarning(
                ex,
                "Wiki update pipeline failed for {AppId} session {SessionId}",
                job.AppId,
                job.SessionId);
        }
    }
}
