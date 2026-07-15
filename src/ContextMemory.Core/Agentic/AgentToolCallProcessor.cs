using System.Diagnostics;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgentToolCallProcessor : IAgentToolCallProcessor
{
    private readonly IEnumerable<IToolExecutor> _toolExecutors;
    private readonly IAgenticPendingStore _pendingStore;
    private readonly ISessionStore _sessionStore;

    public AgentToolCallProcessor(
        IEnumerable<IToolExecutor> toolExecutors,
        IAgenticPendingStore pendingStore,
        ISessionStore sessionStore)
    {
        _toolExecutors = toolExecutors;
        _pendingStore = pendingStore;
        _sessionStore = sessionStore;
    }

    public async Task<AgentToolCallOutcome> ProcessAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        int iteration,
        List<AgentExecutionStep> steps,
        List<OllamaMessage> messages,
        Action<AgenticProgressEvent>? report,
        bool skipConfirmation,
        CancellationToken cancellationToken = default)
    {
        if (!skipConfirmation)
        {
            var destructive = AgenticDestructiveActionDetector.Analyze(
                toolCall,
                runtimeConfig.Agentic.Guardrails);

            if (destructive is not null)
            {
                var pending = new AgenticPendingState
                {
                    PendingId = Guid.NewGuid().ToString("N")[..12],
                    ToolName = toolCall.Function.Name,
                    Arguments = toolCall.Function.Arguments,
                    MatchedKeyword = destructive.Keyword,
                    DefaultLanguage = runtimeConfig.DefaultLanguage,
                    Iteration = iteration,
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
                    Iteration = iteration,
                    ToolName = toolCall.Function.Name,
                    Detail = AgenticConfirmationParser.BuildConfirmationPrompt(pending)
                });

                return new AgentToolCallOutcome
                {
                    AwaitingConfirmation = BuildAwaitingConfirmationResult(pending)
                };
            }
        }

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ToolStarted,
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            Detail = toolCall.Function.Arguments
        });

        var sw = Stopwatch.StartNew();
        var toolResult = await ExecuteToolAsync(toolCall, appId, runtimeConfig, cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();

        var step = new AgentExecutionStep
        {
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            Arguments = toolCall.Function.Arguments,
            Output = toolResult.Output,
            ExitCode = toolResult.ExitCode,
            Success = toolResult.Success,
            Duration = sw.Elapsed
        };
        steps.Add(step);

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ToolCompleted,
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            Step = step
        });

        messages.Add(new OllamaMessage
        {
            Role = "tool",
            Content = AgenticToolObservationFormatter.Format(
                toolCall.Function.Name, toolResult, runtimeConfig)
        });

        return new AgentToolCallOutcome { Result = toolResult };
    }

    private async Task<ToolExecutionResult> ExecuteToolAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var executor = _toolExecutors.FirstOrDefault(e => e.CanExecute(toolCall.Function.Name, runtimeConfig));
        if (executor is null)
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.ToolNotRegistered(toolCall.Function.Name, runtimeConfig),
                ExitCode = 1
            };
        }

        return await executor.ExecuteAsync(toolCall, appId, runtimeConfig, cancellationToken).ConfigureAwait(false);
    }

    private static AgentResult BuildAwaitingConfirmationResult(AgenticPendingState pending) =>
        AgentResult.AwaitingHumanConfirmation(
            AgenticConfirmationParser.BuildConfirmationPrompt(pending),
            pending.PendingId,
            pending.Steps,
            pending.Iteration,
            pending.Kind);

    private static void Report(Action<AgenticProgressEvent>? report, AgenticProgressEvent evt) =>
        report?.Invoke(evt);
}
