using System.Diagnostics;
using System.Net.Http;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Agentic.Prompts;
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
    private readonly IAgentContextCompactor _contextCompactor;
    private readonly ILogger<AgentLoopRunner> _logger;
    private readonly ContextMemoryOptions _options;

    public AgentLoopRunner(
        ILlmAdapterResolver adapterResolver,
        IAgentValidator validator,
        IAgentToolCallProcessor toolCallProcessor,
        ISessionStore sessionStore,
        IAgenticPendingStore pendingStore,
        IAgentContextCompactor contextCompactor,
        ILogger<AgentLoopRunner> logger,
        IOptions<ContextMemoryOptions> options)
    {
        _adapterResolver = adapterResolver;
        _validator = validator;
        _toolCallProcessor = toolCallProcessor;
        _sessionStore = sessionStore;
        _pendingStore = pendingStore;
        _contextCompactor = contextCompactor;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<AgentResult> RunAsync(AgentLoopRequest request, CancellationToken cancellationToken = default)
    {
        var messages = request.Messages;
        var steps = request.Steps;
        var capabilities = LlmCapabilitiesResolver.From(request.RuntimeConfig);
        var maxIterations = LlmCapabilitiesResolver.ResolveMaxIterations(request.RuntimeConfig);
        var loopTimeout = ResolveLoopTimeout(request.RuntimeConfig);
        var loopSw = Stopwatch.StartNew();
        var adapter = _adapterResolver.Resolve(request.RuntimeConfig);
        string? lastAnswer = null;
        var requireToolChoice = false;

        var staticPromptChars = messages
            .FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            ?.Content?.Length ?? 0;
        var compactionCount = 0;
        var llmCalls = 0;

        for (var iteration = request.StartIteration - 1; iteration < maxIterations; iteration++)
        {
            if (loopSw.Elapsed >= loopTimeout)
            {
                _logger.LogWarning(
                    "Agentic loop timed out for {AppId} after {ElapsedMs}ms ({Iterations} iterations)",
                    request.AppId,
                    loopSw.ElapsedMilliseconds,
                    iteration);

                var timeoutResult = AttachDiscovery(
                    BuildTimeoutResult(lastAnswer, steps, iteration, request.RuntimeConfig.DefaultLanguage),
                    messages, steps, staticPromptChars, compactionCount, llmCalls);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.TimedOut,
                    Iteration = iteration,
                    Detail = AgenticMessages.TimeoutAfterIterations(iteration, request.RuntimeConfig.DefaultLanguage)
                });
                return timeoutResult;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var compaction = await _contextCompactor
                .TryCompactAsync(
                    request.AppId,
                    request.UserId,
                    request.SessionId,
                    request.RuntimeConfig,
                    messages,
                    iteration + 1,
                    cancellationToken)
                .ConfigureAwait(false);
            if (compaction is not null)
            {
                compactionCount++;
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Compacting,
                    Iteration = iteration + 1,
                    ArtifactId = compaction.HistoryArtifactId,
                    Detail = $"Compacted ~{compaction.EstimatedTokensBefore} tokens → summary + historyArtifactId"
                });
            }

            Report(request.Report, new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.LlmRequest,
                Iteration = iteration + 1
            });

            var toolsForRequest = request.Tools.Count > 0 ? request.Tools.ToList() : null;
            if (toolsForRequest is not null && capabilities.SanitizeSchemasAggressively)
                toolsForRequest = SanitizeToolSchemas(toolsForRequest);

            var toolChoice = ResolveToolChoice(capabilities, requireToolChoice, toolsForRequest);

            var llmRequest = request.EnrichedRequest with
            {
                Messages = messages,
                Tools = toolsForRequest,
                McpServers = request.McpServers.Count > 0 ? request.McpServers.ToList() : null,
                Stream = false,
                ToolChoice = toolChoice
            };

            OllamaResponse response;
            try
            {
                response = await adapter.ChatAsync(llmRequest, cancellationToken).ConfigureAwait(false);
                llmCalls++;
            }
            catch (HttpRequestException ex) when (IsLlmGrammarError(ex))
            {
                _logger.LogWarning(
                    ex,
                    "LLM rejected tool grammars for {AppId}; retrying with simplified MCP parameter schemas",
                    request.AppId);

                var simplifiedTools = SimplifyToolSchemas(llmRequest.Tools);
                var retryRequest = llmRequest with { Tools = simplifiedTools };
                try
                {
                    response = await adapter.ChatAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                    llmCalls++;
                }
                catch (HttpRequestException retryEx) when (IsLlmGrammarError(retryEx))
                {
                    _logger.LogError(retryEx, "LLM grammar still failing for {AppId} after schema simplify", request.AppId);
                    throw new InvalidOperationException(
                        "O modelo LLM rejeitou os schemas das tools (grammar). " +
                        "Reduza maxMcpToolsPerTurn ou simplifique as tools MCP.",
                        retryEx);
                }
            }

            var assistantMessage = response.Message;

            if (capabilities.EnableProseToolCallPromotion
                && assistantMessage is not null
                && (assistantMessage.ToolCalls is null || assistantMessage.ToolCalls.Count == 0))
            {
                var promoted = ProseToolCallParser.TryParse(
                    OllamaLlmText.GetMessageContent(assistantMessage));
                if (promoted is { Count: > 0 })
                {
                    _logger.LogInformation(
                        "Promoted {Count} prose tool call(s) to structured tool_calls for {AppId}",
                        promoted.Count,
                        request.AppId);
                    assistantMessage = assistantMessage with
                    {
                        ToolCalls = promoted.ToList(),
                        Content = string.Empty
                    };
                }
            }

            if (assistantMessage?.ToolCalls is { Count: > 0 } toolCalls)
            {
                requireToolChoice = false;
                messages.Add(assistantMessage);

                foreach (var toolCall in toolCalls)
                {
                    if (loopSw.Elapsed >= loopTimeout)
                    {
                        var timeoutResult = AttachDiscovery(
                            BuildTimeoutResult(lastAnswer, steps, iteration + 1, request.RuntimeConfig.DefaultLanguage),
                            messages, steps, staticPromptChars, compactionCount, llmCalls);
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
                    {
                        return AttachDiscovery(
                            toolOutcome.AwaitingConfirmation,
                            messages, steps, staticPromptChars, compactionCount, llmCalls);
                    }
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
                var success = AttachDiscovery(
                    AgentResult.Succeeded(lastAnswer, steps, iteration + 1),
                    messages, steps, staticPromptChars, compactionCount, llmCalls);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Completed,
                    Iteration = iteration + 1,
                    Detail = AgenticMessages.LoopCompleted(iteration + 1, steps.Count, request.RuntimeConfig)
                });
                return success;
            }

            // Next turn: prefer forcing a tool call when the model answered without evidence.
            requireToolChoice = request.Tools.Count > 0;

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
            var review = await RequestHumanReviewAsync(
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
            return AttachDiscovery(review, messages, steps, staticPromptChars, compactionCount, llmCalls);
        }

        Report(request.Report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.MaxIterations,
            Detail = AgenticMessages.MaxIterationsReached(maxIterations, request.RuntimeConfig.DefaultLanguage)
        });

        return AttachDiscovery(
            AgentResult.LimitReached(fallback, steps, maxIterations),
            messages, steps, staticPromptChars, compactionCount, llmCalls);
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

    private static AgentResult AttachDiscovery(
        AgentResult result,
        List<OllamaMessage> messages,
        List<AgentExecutionStep> steps,
        int staticPromptChars,
        int compactionCount,
        int llmCalls)
    {
        var toolObservationChars = messages
            .Where(m => string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            .Sum(m => m.Content?.Length ?? 0);

        var discoveryFetchedChars = steps
            .Where(s => IsDiscoveryFetchTool(s.ToolName))
            .Sum(s => s.Output?.Length ?? 0);

        return result.WithDiscovery(DiscoveryTelemetry.FromCounts(
            staticPromptChars,
            discoveryFetchedChars,
            toolObservationChars,
            compactionCount,
            llmCalls));
    }

    private static bool IsDiscoveryFetchTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        if (toolName.StartsWith("wiki_", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(toolName, SessionDiscoveryTools.SkillRead, StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, SessionDiscoveryTools.ArtifactRead, StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, SessionDiscoveryTools.ArtifactTail, StringComparison.OrdinalIgnoreCase);
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

    private static bool IsLlmGrammarError(HttpRequestException ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("failed to parse grammar", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("Failed to initialize samplers", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveToolChoice(
        LlmCapabilities capabilities,
        bool requireToolChoice,
        List<OllamaTool>? tools)
    {
        if (tools is null || tools.Count == 0)
            return null;

        if (requireToolChoice)
            return "required";

        return string.IsNullOrWhiteSpace(capabilities.DefaultToolChoice)
            ? null
            : capabilities.DefaultToolChoice;
    }

    private static List<OllamaTool> SanitizeToolSchemas(List<OllamaTool> tools) =>
        tools
            .Select(t => new OllamaTool(
                t.Type,
                new OllamaFunction(
                    t.Function.Name,
                    t.Function.Description,
                    McpInputSchemaSanitizer.Sanitize(t.Function.Parameters))))
            .ToList();

    private static List<OllamaTool>? SimplifyToolSchemas(List<OllamaTool>? tools)
    {
        if (tools is null || tools.Count == 0)
            return tools;

        // Aggressive fallback: keep tool names/descriptions, drop complex parameter grammars.
        object minimal = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
            ["additionalProperties"] = true
        };

        return tools
            .Select(t => new OllamaTool(
                t.Type,
                new OllamaFunction(t.Function.Name, t.Function.Description, minimal)))
            .ToList();
    }
}
