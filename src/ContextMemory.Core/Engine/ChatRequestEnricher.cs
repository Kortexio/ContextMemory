using ContextMemory.Core.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Core.Utilities;
using ContextMemory.Core.WebSearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Engine;

public sealed class ChatRequestEnricher : IChatRequestEnricher
{
    private readonly ISessionStore _sessionStore;
    private readonly ISessionArtifactStore _artifacts;
    private readonly WebSearchEnricher _webSearchEnricher;
    private readonly ISystemPromptBuilder _systemPromptBuilder;
    private readonly GlobalWikiService _globalWikiService;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<ChatRequestEnricher> _logger;

    public ChatRequestEnricher(
        ISessionStore sessionStore,
        ISessionArtifactStore artifacts,
        WebSearchEnricher webSearchEnricher,
        ISystemPromptBuilder systemPromptBuilder,
        GlobalWikiService globalWikiService,
        IOptions<ContextMemoryOptions> options,
        ILogger<ChatRequestEnricher> logger)
    {
        _sessionStore = sessionStore;
        _artifacts = artifacts;
        _webSearchEnricher = webSearchEnricher;
        _systemPromptBuilder = systemPromptBuilder;
        _globalWikiService = globalWikiService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(OllamaRequest Request, OllamaMessage? LastUser, int PromptTokens)> EnrichAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        AppRuntimeConfig runtimeConfig,
        ChatTurnContext turnContext,
        CancellationToken cancellationToken = default)
    {
        var schema = string.IsNullOrWhiteSpace(runtimeConfig.WikiSchema)
            ? SessionDefaults.DefaultSchema
            : runtimeConfig.WikiSchema;

        await _sessionStore.EnsureInitializedAsync(appId, userId, sessionId, schema, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = await _sessionStore.LoadAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
        var lastUser = request.GetLastUserMessage();

        var webEnrichment = await _webSearchEnricher
            .TryEnrichAsync(appId, lastUser?.Content, snapshot, runtimeConfig.WebSearch, runtimeConfig.DefaultLanguage, cancellationToken)
            .ConfigureAwait(false);
        turnContext.WebSearch = webEnrichment;

        var digestsMarkdown = await TryLoadGlobalDigestsAsync(
                appId, lastUser?.Content, runtimeConfig, cancellationToken)
            .ConfigureAwait(false);

        string? rollingSummary = null;
        try
        {
            rollingSummary = await _artifacts
                .ReadAsync(appId, userId, sessionId, AgentContextCompactor.RollingSummaryArtifactId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Rolling summary load skipped for {SessionId}", sessionId);
        }

        var systemPrompt = _systemPromptBuilder.Build(
            appId,
            runtimeConfig,
            snapshot,
            lastUser?.Content,
            webEnrichment.PromptMarkdown,
            digestsMarkdown,
            rollingSummary);

        var messages = new List<OllamaMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(snapshot.Messages);

        if (lastUser is not null && !MessageAlreadyInHistory(snapshot.Messages, lastUser))
            messages.Add(lastUser);

        var model = string.IsNullOrWhiteSpace(request.Model) ? runtimeConfig.LlmModel : request.Model;
        var mergedOptions = LlmGenerationConfig.MergeOptions(runtimeConfig.LlmOptions, request.Options);
        var enriched = request with
        {
            Model = model,
            Messages = messages,
            Options = mergedOptions,
            KeepAlive = LlmGenerationConfig.MergeKeepAlive(runtimeConfig.LlmOptions, request.KeepAlive),
            Format = LlmGenerationConfig.MergeFormat(runtimeConfig.LlmOptions, request.Format),
            // Tenant policy wins: off by default; /v1 maps false → reasoning_effort=none.
            Think = runtimeConfig.LlmThinkEnabled
        };

        var prepared = (enriched, lastUser, TokenEstimator.Estimate(messages));
        turnContext.Prepared = prepared;
        return prepared;
    }

    private async Task<string?> TryLoadGlobalDigestsAsync(
        string appId,
        string? userQuery,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        if (!runtimeConfig.GlobalWikiEnabled || string.IsNullOrWhiteSpace(userQuery))
            return null;

        try
        {
            var topK = SessionWikiSettings.ResolveDigestTopK(runtimeConfig, _options);
            var budget = SessionWikiSettings.ResolveMaxDigestContextChars(runtimeConfig, _options);
            var result = await _globalWikiService.QueryAsync(
                appId,
                new GlobalWikiQueryRequest
                {
                    Query = userQuery.Trim(),
                    TopK = topK,
                    BudgetChars = budget,
                    DigestOnly = true,
                    IncludeIndex = false
                },
                budget,
                cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(result.CompiledMarkdown) ? null : result.CompiledMarkdown;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Global wiki digest inject failed for {AppId}; continuing without digests", appId);
            return null;
        }
    }

    private static bool MessageAlreadyInHistory(IReadOnlyList<OllamaMessage> history, OllamaMessage lastUser) =>
        history.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
        == lastUser.Content;
}
