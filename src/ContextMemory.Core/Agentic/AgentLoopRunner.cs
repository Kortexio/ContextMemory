using System.Diagnostics;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Agentic;

public sealed class AgentLoopRunner : IAgentLoopRunner
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAgentValidator _validator;
    private readonly IAgentToolCallProcessor _toolCallProcessor;
    private readonly ISessionStore _sessionStore;
    private readonly IAgenticPendingStore _pendingStore;
    private readonly ILogger<AgentLoopRunner> _logger;
    private readonly ContextMemoryOptions _options;

    public AgentLoopRunner(
        ILlmAdapterResolver adapterResolver,
        IAgentValidator validator,
        IAgentToolCallProcessor toolCallProcessor,
        ISessionStore sessionStore,
        IAgenticPendingStore pendingStore,
        ILogger<AgentLoopRunner> logger,
        IOptions<ContextMemoryOptions> options)
    {
        _adapterResolver = adapterResolver;
        _validator = validator;
        _toolCallProcessor = toolCallProcessor;
        _sessionStore = sessionStore;
        _pendingStore = pendingStore;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<AgentResult> RunAsync(AgentLoopRequest request, CancellationToken cancellationToken = default)
    {
        var messages = request.Messages;
        var steps = request.Steps;
        var maxIterations = request.RuntimeConfig.Agentic.MaxIterations;
        var loopTimeout = ResolveLoopTimeout(request.RuntimeConfig);
        var loopSw = Stopwatch.StartNew();
        var adapter = _adapterResolver.Resolve(request.RuntimeConfig.LlmBackend);
        string? lastAnswer = null;

        for (var iteration = request.StartIteration - 1; iteration < maxIterations; iteration++)
        {
            if (loopSw.Elapsed >= loopTimeout)
            {
                _logger.LogWarning(
                    "Agentic loop timed out for {AppId} after {ElapsedMs}ms ({Iterations} iterations)",
                    request.AppId,
                    loopSw.ElapsedMilliseconds,
                    iteration);

                var timeoutResult = BuildTimeoutResult(
                    lastAnswer, steps, iteration, request.RuntimeConfig.DefaultLanguage);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.TimedOut,
                    Iteration = iteration,
                    Detail = AgenticMessages.TimeoutAfterIterations(iteration, request.RuntimeConfig.DefaultLanguage)
                });
                return timeoutResult;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(request.Report, new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.LlmRequest,
                Iteration = iteration + 1
            });

            var llmRequest = request.EnrichedRequest with
            {
                Messages = messages,
                Tools = request.Tools.Count > 0 ? request.Tools.ToList() : null,
                McpServers = request.McpServers.Count > 0 ? request.McpServers.ToList() : null,
                Stream = false
            };

            var response = await adapter.ChatAsync(llmRequest, cancellationToken).ConfigureAwait(false);
            var assistantMessage = response.Message;

            if (assistantMessage?.ToolCalls is { Count: > 0 } toolCalls)
            {
                messages.Add(assistantMessage);

                foreach (var toolCall in toolCalls)
                {
                    if (loopSw.Elapsed >= loopTimeout)
                    {
                        var timeoutResult = BuildTimeoutResult(
                            lastAnswer, steps, iteration + 1, request.RuntimeConfig.DefaultLanguage);
                        Report(request.Report, new AgenticProgressEvent
                        {
                            Phase = AgenticProgressPhase.TimedOut,
                            Iteration = iteration + 1,
                            Detail = AgenticMessages.ToolTimeout(request.RuntimeConfig)
                        });
                        return timeoutResult;
                    }

                    var toolOutcome = await _toolCallProcessor
                        .ProcessAsync(
                            toolCall,
                            request.AppId,
                            request.UserId,
                            request.SessionId,
                            request.RuntimeConfig,
                            iteration + 1,
                            steps,
                            messages,
                            request.Report,
                            skipConfirmation: false,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (toolOutcome.AwaitingConfirmation is not null)
                        return toolOutcome.AwaitingConfirmation;
                }

                continue;
            }

            lastAnswer = OllamaLlmText.NormalizeAssistantContent(
                OllamaLlmText.GetMessageContent(assistantMessage));

            Report(request.Report, new AgenticProgressEvent { Phase = AgenticProgressPhase.Validating });

            var validation = await _validator.ValidateAsync(
                    new AgentValidationRequest
                    {
                        FinalAnswer = lastAnswer,
                        Steps = steps,
                        RuntimeConfig = request.RuntimeConfig,
                        UserObjective = request.EnrichedRequest.Messages.GetLastUserMessage()?.Content
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (validation.IsValid)
            {
                var success = AgentResult.Succeeded(lastAnswer, steps, iteration + 1);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Completed,
                    Iteration = iteration + 1,
                    Detail = AgenticMessages.LoopCompleted(iteration + 1, steps.Count, request.RuntimeConfig)
                });
                return success;
            }

            Report(request.Report, new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.ValidationRejected,
                Iteration = iteration + 1,
                Detail = validation.FeedbackForModel
            });

            messages.Add(new OllamaMessage { Role = "assistant", Content = lastAnswer });
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = validation.FeedbackForModel ?? AgenticMessages.InvalidResponseRetry(request.RuntimeConfig)
            });
        }

        var fallback = lastAnswer
            ?? AgenticMessages.MaxIterationsExceeded(request.RuntimeConfig)
            + AgenticMessages.MaxIterationsFallbackSuffix(request.RuntimeConfig.DefaultLanguage);

        if (request.RuntimeConfig.Agentic.Guardrails.HumanReviewOnMaxIterations)
        {
            return await RequestHumanReviewAsync(
                    request.AppId,
                    request.UserId,
                    request.SessionId,
                    maxIterations,
                    fallback,
                    request.RuntimeConfig.DefaultLanguage,
                    steps,
                    messages,
                    request.Report,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Report(request.Report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.MaxIterations,
            Detail = AgenticMessages.MaxIterationsReached(maxIterations, request.RuntimeConfig.DefaultLanguage)
        });

        return AgentResult.LimitReached(fallback, steps, maxIterations);
    }

    private async Task<AgentResult> RequestHumanReviewAsync(
        string appId,
        string userId,
        string sessionId,
        int maxIterations,
        string fallback,
        string defaultLanguage,
        List<AgentExecutionStep> steps,
        List<OllamaMessage> messages,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var pending = new AgenticPendingState
        {
            PendingId = Guid.NewGuid().ToString("N")[..12],
            Kind = AgenticPendingKinds.MaxIterations,
            ToolName = "_human_review",
            Arguments = "{}",
            MatchedKeyword = "max-iterations",
            DefaultLanguage = defaultLanguage,
            Iteration = maxIterations,
            PartialAnswer = fallback,
            Steps = steps.ToList(),
            Messages = messages.ToList()
        };

        await AgenticConfirmationCheckpoint
            .WritePendingAsync(_sessionStore, appId, userId, sessionId, pending, cancellationToken)
            .ConfigureAwait(false);
        await _pendingStore
            .SaveAsync(appId, userId, sessionId, pending, cancellationToken)
            .ConfigureAwait(false);

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.AwaitingConfirmation,
            Iteration = maxIterations,
            Detail = AgenticConfirmationParser.BuildConfirmationPrompt(pending)
        });

        return AgentResult.AwaitingHumanConfirmation(
            AgenticConfirmationParser.BuildConfirmationPrompt(pending),
            pending.PendingId,
            pending.Steps,
            pending.Iteration,
            pending.Kind);
    }

    private static void Report(Action<AgenticProgressEvent>? report, AgenticProgressEvent evt) =>
        report?.Invoke(evt);

    private TimeSpan ResolveLoopTimeout(AppRuntimeConfig runtimeConfig)
    {
        var seconds = runtimeConfig.Agentic.Guardrails.LoopTimeoutSeconds > 0
            ? runtimeConfig.Agentic.Guardrails.LoopTimeoutSeconds
            : _options.DefaultAgenticLoopTimeoutSeconds;

        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    private static AgentResult BuildTimeoutResult(
        string? lastAnswer,
        List<AgentExecutionStep> steps,
        int iterations,
        string? language) =>
        AgentResult.TimedOutPartial(
            AgentPartialResponseFormatter.FormatTimeoutResponse(lastAnswer, steps, language),
            steps,
            iterations);
}
