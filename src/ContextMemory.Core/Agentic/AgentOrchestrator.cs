using System.Diagnostics;
using System.Threading.Channels;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IAgenticToolRegistry _toolRegistry;
    private readonly IAgentConfirmationFlow _confirmationFlow;
    private readonly IAgentToolCallProcessor _toolCallProcessor;
    private readonly IAgentLoopRunner _loopRunner;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IAgenticToolRegistry toolRegistry,
        IAgentConfirmationFlow confirmationFlow,
        IAgentToolCallProcessor toolCallProcessor,
        IAgentLoopRunner loopRunner,
        ILogger<AgentOrchestrator> logger)
    {
        _toolRegistry = toolRegistry;
        _confirmationFlow = confirmationFlow;
        _toolCallProcessor = toolCallProcessor;
        _loopRunner = loopRunner;
        _logger = logger;
    }

    public Task<AgentResult> RunAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enrichedRequest,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(appId, userId, sessionId, enrichedRequest, runtimeConfig, report: null, cancellationToken);

    public async IAsyncEnumerable<AgenticOrchestratorEvent> RunWithProgressAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enrichedRequest,
        AppRuntimeConfig runtimeConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<AgenticProgressEvent>();

        async Task<AgentResult> RunSafeAsync()
        {
            try
            {
                return await RunCoreAsync(
                    appId,
                    userId,
                    sessionId,
                    enrichedRequest,
                    runtimeConfig,
                    evt => channel.Writer.TryWrite(evt),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }

        var runTask = RunSafeAsync();

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return AgenticOrchestratorEvent.FromProgress(evt);

        var result = await runTask.ConfigureAwait(false);
        yield return AgenticOrchestratorEvent.FromResult(result);
    }

    private async Task<AgentResult> RunCoreAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enrichedRequest,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var tools = (await _toolRegistry.BuildToolsAsync(runtimeConfig, cancellationToken).ConfigureAwait(false)).ToList();
        var toolNamesSummary = await _toolRegistry.BuildToolNamesSummaryAsync(runtimeConfig, cancellationToken)
            .ConfigureAwait(false);
        var mcpServers = _toolRegistry.BuildMcpServers(runtimeConfig);
        var messages = AgentInstructionInjector.Inject(enrichedRequest.Messages, runtimeConfig, toolNamesSummary);
        var steps = new List<AgentExecutionStep>();

        var lastUserMessage = enrichedRequest.Messages.GetLastUserMessage()?.Content;

        var confirmationOutcome = await _confirmationFlow
            .TryResolvePendingAsync(appId, userId, sessionId, lastUserMessage, report, cancellationToken)
            .ConfigureAwait(false);

        if (confirmationOutcome.IsResolved)
            return confirmationOutcome.Result!;

        if (confirmationOutcome.ResumeMessages is not null)
        {
            messages = confirmationOutcome.ResumeMessages;
            steps = confirmationOutcome.ResumeSteps!;
        }

        Report(report, new AgenticProgressEvent { Phase = AgenticProgressPhase.Started });

        var startIteration = confirmationOutcome.ConfirmedPending?.Iteration ?? 1;
        if (confirmationOutcome.ConfirmedPending is { } confirmedPending)
        {
            var resumeToolCall = new OllamaToolCall(
                new OllamaFunctionCall(confirmedPending.ToolName, confirmedPending.Arguments));

            var resumeResult = await _toolCallProcessor
                .ProcessAsync(
                    resumeToolCall,
                    appId,
                    userId,
                    sessionId,
                    runtimeConfig,
                    confirmedPending.Iteration,
                    steps,
                    messages,
                    report,
                    skipConfirmation: true,
                    cancellationToken)
                .ConfigureAwait(false);

            if (resumeResult.AwaitingConfirmation is not null)
                return resumeResult.AwaitingConfirmation;

            startIteration = confirmedPending.Iteration;
        }

        _logger.LogDebug(
            "Starting agentic loop for {AppId} session {SessionId} from iteration {StartIteration}",
            appId,
            sessionId,
            startIteration);

        return await _loopRunner.RunAsync(
            new AgentLoopRequest
            {
                AppId = appId,
                UserId = userId,
                SessionId = sessionId,
                EnrichedRequest = enrichedRequest,
                RuntimeConfig = runtimeConfig,
                Messages = messages,
                Steps = steps,
                Tools = tools,
                McpServers = mcpServers,
                StartIteration = startIteration,
                Report = report
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void Report(Action<AgenticProgressEvent>? report, AgenticProgressEvent evt) =>
        report?.Invoke(evt);
}
