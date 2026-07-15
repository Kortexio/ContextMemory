using System.Diagnostics;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Exceptions;
using ContextMemory.Core.Models;
using ContextMemory.Core.Utilities;

namespace ContextMemory.Core.Engine;

public sealed class ContextEngine : IContextEngine
{
    private readonly IAppRegistry _appRegistry;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IChatRequestEnricher _chatRequestEnricher;
    private readonly IChatPostProcessor _chatPostProcessor;
    private readonly ChatTurnContext _chatTurnContext;
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IMessageIdTracker _messageIdTracker;
    private readonly ITelemetryCollector _telemetry;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IAgentExecutionLogger _agentExecutionLogger;
    private readonly IAgenticUsageCharger _agenticUsageCharger;

    public ContextEngine(
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IChatRequestEnricher chatRequestEnricher,
        IChatPostProcessor chatPostProcessor,
        ChatTurnContext chatTurnContext,
        ILlmAdapterResolver adapterResolver,
        IMessageIdTracker messageIdTracker,
        ITelemetryCollector telemetry,
        IAgentOrchestrator agentOrchestrator,
        IAgentExecutionLogger agentExecutionLogger,
        IAgenticUsageCharger agenticUsageCharger)
    {
        _appRegistry = appRegistry;
        _appConfigStore = appConfigStore;
        _chatRequestEnricher = chatRequestEnricher;
        _chatPostProcessor = chatPostProcessor;
        _chatTurnContext = chatTurnContext;
        _adapterResolver = adapterResolver;
        _messageIdTracker = messageIdTracker;
        _telemetry = telemetry;
        _agentOrchestrator = agentOrchestrator;
        _agentExecutionLogger = agentExecutionLogger;
        _agenticUsageCharger = agenticUsageCharger;
    }

    public async Task<ChatPipelineResult> ProcessChatAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        EnsureAppExists(appId);

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var (enriched, lastUser, promptTokens) = await _chatRequestEnricher
            .EnrichAsync(appId, userId, sessionId, request, runtimeConfig, _chatTurnContext, cancellationToken)
            .ConfigureAwait(false);

        var adapter = _adapterResolver.Resolve(runtimeConfig.LlmBackend);
        OllamaResponse response;
        string assistantContent;

        if (runtimeConfig.AgenticEnabled)
        {
            var agentResult = await RunAgenticTurnAsync(
                    appId, userId, sessionId, enriched, lastUser, runtimeConfig, cancellationToken)
                .ConfigureAwait(false);
            assistantContent = agentResult.FinalAnswer;
            response = new OllamaResponse
            {
                Model = enriched.Model,
                Message = new OllamaMessage { Role = "assistant", Content = assistantContent },
                Done = true,
                ContextMemory = new ContextMemoryMetadata
                {
                    Agentic = AgenticStreamMetadata.FromResult(agentResult, runtimeConfig.DefaultLanguage)
                }
            };
            _chatTurnContext.AgenticResult = agentResult;
        }
        else
        {
            response = await adapter.ChatAsync(enriched, cancellationToken).ConfigureAwait(false);
            assistantContent = OllamaLlmText.NormalizeAssistantContent(
                OllamaLlmText.GetMessageContent(response.Message));
        }

        var completionTokens = TokenEstimator.Estimate(assistantContent);
        var messageId = await _chatPostProcessor
            .PostProcessAsync(
                appId, userId, sessionId, lastUser, assistantContent, runtimeConfig, _chatTurnContext,
                messageId: null, cancellationToken)
            .ConfigureAwait(false);

        _telemetry.RecordRequest(appId, userId, 200, sw.ElapsedMilliseconds, promptTokens, completionTokens);

        return new ChatPipelineResult
        {
            Response = response,
            MessageId = messageId,
            EstimatedPromptTokens = promptTokens,
            EstimatedCompletionTokens = completionTokens
        };
    }

    public async IAsyncEnumerable<OllamaResponse> ProcessChatStreamAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureAppExists(appId);

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var (enriched, _, _) = _chatTurnContext.Prepared
            ?? await _chatRequestEnricher
                .EnrichAsync(appId, userId, sessionId, request, runtimeConfig, _chatTurnContext, cancellationToken)
                .ConfigureAwait(false);

        if (runtimeConfig.AgenticEnabled)
        {
            AgentResult? agentResult = _chatTurnContext.AgenticResult;

            if (agentResult is null)
            {
                await foreach (var evt in _agentOrchestrator
                    .RunWithProgressAsync(appId, userId, sessionId, enriched, runtimeConfig, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (evt.Progress is not null)
                        yield return AgenticProgressChunkMapper.ToProgressChunk(
                            enriched.Model, evt.Progress, runtimeConfig.DefaultLanguage);

                    if (evt.Result is not null)
                    {
                        agentResult = evt.Result;
                        await _agentExecutionLogger
                            .LogAsync(
                                appId,
                                userId,
                                sessionId,
                                request.GetLastUserMessage()?.Content,
                                agentResult,
                                cancellationToken)
                            .ConfigureAwait(false);
                        _chatTurnContext.AgenticResult = agentResult;
                    }
                }
            }

            if (agentResult is not null)
            {
                _agenticUsageCharger.TryCharge(appId, runtimeConfig, agentResult, _chatTurnContext);

                foreach (var chunk in AgenticStreamBuffer.Stream(enriched.Model, agentResult.FinalAnswer))
                    yield return chunk;
            }

            yield break;
        }

        var adapter = _adapterResolver.Resolve(runtimeConfig.LlmBackend);
        await foreach (var chunk in adapter.ChatStreamAsync(enriched, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public async Task<ChatPipelineResult> FinalizeStreamAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        string assistantContent,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAppExists(appId);

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var lastUser = request.GetLastUserMessage();
        var resolvedMessageId = await _chatPostProcessor
            .PostProcessAsync(
                appId, userId, sessionId, lastUser, assistantContent, runtimeConfig, _chatTurnContext,
                messageId, cancellationToken)
            .ConfigureAwait(false);

        return new ChatPipelineResult
        {
            Response = new OllamaResponse
            {
                Model = request.Model,
                Message = new OllamaMessage { Role = "assistant", Content = assistantContent },
                Done = true
            },
            MessageId = resolvedMessageId,
            EstimatedCompletionTokens = TokenEstimator.Estimate(assistantContent)
        };
    }

    public string? ReserveStreamMessageId(string appId, string userId, OllamaRequest request)
    {
        if (request.GetLastUserMessage() is null)
            return null;

        return _messageIdTracker.CreateAndTrack(appId, userId);
    }

    public async Task PrimeStreamTurnAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_chatTurnContext.Prepared is not null)
            return;

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        await _chatRequestEnricher
            .EnrichAsync(appId, userId, sessionId, request, runtimeConfig, _chatTurnContext, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AgentResult> RunAgenticTurnAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enriched,
        OllamaMessage? lastUser,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var agentResult = await _agentOrchestrator
            .RunAsync(appId, userId, sessionId, enriched, runtimeConfig, cancellationToken)
            .ConfigureAwait(false);

        await _agentExecutionLogger
            .LogAsync(appId, userId, sessionId, lastUser?.Content, agentResult, cancellationToken)
            .ConfigureAwait(false);

        _agenticUsageCharger.TryCharge(appId, runtimeConfig, agentResult, _chatTurnContext);

        return agentResult;
    }

    private void EnsureAppExists(string appId)
    {
        if (!_appRegistry.TryGetApp(appId, out _))
            throw new AppNotFoundException(appId);
    }
}
