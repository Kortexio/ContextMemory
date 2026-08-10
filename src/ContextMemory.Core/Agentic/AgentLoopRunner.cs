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
        EnsureUserMessagePresent(messages, request);
        var steps = request.Steps;
        var capabilities = LlmCapabilitiesResolver.From(request.RuntimeConfig);
        var maxIterations = LlmCapabilitiesResolver.ResolveMaxIterations(request.RuntimeConfig);
        var loopTimeout = ResolveLoopTimeout(request.RuntimeConfig);
        var loopSw = Stopwatch.StartNew();
        var adapter = _adapterResolver.Resolve(request.RuntimeConfig);
        string? lastAnswer = null;
        var requireToolChoice = false;
        var promotedProseToolCalls = 0;
        var schemaRepairLevel = "none";
        var resolvedProfile = AgenticPromptProfileResolver.Resolve(request.RuntimeConfig).ToString();

        if (request.Tools.Count > 0
            && !string.IsNullOrWhiteSpace(request.EnrichedRequest.Format)
            && capabilities.SupportsOpenAiJsonFormat)
        {
            _logger.LogInformation(
                "Ignoring llm format={Format} for agentic turn with tools on {AppId} (tool_calls conflict with response_format)",
                request.EnrichedRequest.Format,
                request.AppId);
        }

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
                    messages, steps, staticPromptChars, compactionCount, llmCalls,
                    promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.TimedOut,
                    Iteration = iteration,
                    Detail = AgenticMessages.TimeoutAfterIterations(iteration, request.RuntimeConfig.DefaultLanguage)
                });
                return timeoutResult;
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureUserMessagePresent(messages, request);

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
            {
                toolsForRequest = SanitizeToolSchemas(toolsForRequest);
                schemaRepairLevel = MaxRepairLevel(schemaRepairLevel, "sanitize");
            }

            var toolChoice = ResolveToolChoice(capabilities, requireToolChoice, toolsForRequest, request.RuntimeConfig);

            // format=json fights native tool_calls — clear on agentic iterations with tools.
            var format = toolsForRequest is { Count: > 0 }
                ? null
                : request.EnrichedRequest.Format;

            var llmRequest = request.EnrichedRequest with
            {
                Messages = messages,
                Tools = toolsForRequest,
                McpServers = request.McpServers.Count > 0 ? request.McpServers.ToList() : null,
                Stream = false,
                ToolChoice = toolChoice,
                Format = format
            };

            OllamaResponse response;
            try
            {
                response = await adapter.ChatAsync(llmRequest, cancellationToken).ConfigureAwait(false);
                llmCalls++;
            }
            catch (HttpRequestException ex) when (IsStrictChatTemplateError(ex))
            {
                _logger.LogError(
                    ex,
                    "LLM chat template rejected messages for {AppId} (Qwen/Bonsai-style Jinja). Ensure a user message exists and avoid dual system roles; consider patching the model TEMPLATE.",
                    request.AppId);
                throw new InvalidOperationException(
                    "O modelo rejeitou o chat template (ex. 'No user query found in messages'). "
                    + "Confirma que existe uma mensagem user e um único system; packs Qwen/Bonsai estritos podem precisar de TEMPLATE patch.",
                    ex);
            }
            catch (HttpRequestException ex) when (IsLlmGrammarError(ex))
            {
                _logger.LogWarning(
                    ex,
                    "LLM rejected tool grammars for {AppId}; applying graduated schema repair",
                    request.AppId);

                response = await ChatWithGraduatedRepairAsync(
                        adapter,
                        llmRequest,
                        request.AppId,
                        repairLevel => schemaRepairLevel = MaxRepairLevel(schemaRepairLevel, repairLevel),
                        () => llmCalls++,
                        cancellationToken)
                    .ConfigureAwait(false);
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
                    promotedProseToolCalls += promoted.Count;
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
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
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
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
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
                    messages, steps, staticPromptChars, compactionCount, llmCalls,
                    promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
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
            return AttachDiscovery(
                review, messages, steps, staticPromptChars, compactionCount, llmCalls,
                promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
        }

        Report(request.Report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.MaxIterations,
            Detail = AgenticMessages.MaxIterationsReached(maxIterations, request.RuntimeConfig.DefaultLanguage)
        });

        return AttachDiscovery(
            AgentResult.LimitReached(fallback, steps, maxIterations),
            messages, steps, staticPromptChars, compactionCount, llmCalls,
            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel);
    }

    private async Task<OllamaResponse> ChatWithGraduatedRepairAsync(
        ILlmAdapter adapter,
        OllamaRequest llmRequest,
        string appId,
        Action<string> setRepairLevel,
        Action bumpLlmCalls,
        CancellationToken cancellationToken)
    {
        // sanitize already applied by caller when aggressive — try strip required next.
        setRepairLevel("strip_required");
        var stripped = StripRequiredToolSchemas(llmRequest.Tools);
        var stripRequest = llmRequest with { Tools = stripped };
        try
        {
            var response = await adapter.ChatAsync(stripRequest, cancellationToken).ConfigureAwait(false);
            bumpLlmCalls();
            return response;
        }
        catch (HttpRequestException stripEx) when (IsLlmGrammarError(stripEx))
        {
            _logger.LogWarning(stripEx, "Schema strip_required still failing for {AppId}; simplifying", appId);
        }

        setRepairLevel("simplify");
        var simplifiedTools = SimplifyToolSchemas(llmRequest.Tools);
        var retryRequest = llmRequest with { Tools = simplifiedTools };
        try
        {
            var response = await adapter.ChatAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            bumpLlmCalls();
            return response;
        }
        catch (HttpRequestException retryEx) when (IsLlmGrammarError(retryEx))
        {
            _logger.LogError(retryEx, "LLM grammar still failing for {AppId} after schema simplify", appId);
            throw new InvalidOperationException(
                "O modelo LLM rejeitou os schemas das tools (grammar). "
                + "Reduza maxMcpToolsPerTurn ou simplifique as tools MCP.",
                retryEx);
        }
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
        int llmCalls,
        int promotedProseToolCalls,
        string? resolvedPromptProfile,
        string? harnessMode,
        string? schemaRepairLevel)
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
            llmCalls,
            promotedProseToolCalls,
            resolvedPromptProfile,
            harnessMode,
            schemaRepairLevel));
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

    private static bool IsStrictChatTemplateError(HttpRequestException ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("No user query found in messages", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("System message must be at the beginning", StringComparison.OrdinalIgnoreCase)
               || (msg.Contains("Jinja", StringComparison.OrdinalIgnoreCase)
                   && msg.Contains("raise_exception", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveToolChoice(
        LlmCapabilities capabilities,
        bool requireToolChoice,
        List<OllamaTool>? tools,
        AppRuntimeConfig runtimeConfig)
    {
        if (tools is null || tools.Count == 0)
            return null;

        if (requireToolChoice)
        {
            if (capabilities.HarnessMode == ModelHarnessMode.Strong)
            {
                var hasMcp = runtimeConfig.Agentic.Tools.Integrations.Any(i =>
                    i.Enabled && string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase) && i.IsConfigured);
                return hasMcp ? "required" : "auto";
            }

            return "required";
        }

        return string.IsNullOrWhiteSpace(capabilities.DefaultToolChoice)
            ? null
            : capabilities.DefaultToolChoice;
    }

    private static void EnsureUserMessagePresent(List<OllamaMessage> messages, AgentLoopRequest request)
    {
        var hasRealUser = messages.Any(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(m.Content)
            && !IsToolResponseWrapped(m.Content));

        if (hasRealUser)
            return;

        var objective = request.EnrichedRequest.Messages.GetLastUserMessage()?.Content
            ?? request.Messages.GetLastUserMessage()?.Content
            ?? "Continue. Prefer tools for live data.";

        messages.Add(new OllamaMessage { Role = "user", Content = objective });
    }

    private static bool IsToolResponseWrapped(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var trimmed = content.Trim();
        return trimmed.StartsWith("<tool_response>", StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith("</tool_response>", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaxRepairLevel(string current, string next)
    {
        static int Rank(string level) => level switch
        {
            "simplify" => 3,
            "strip_required" => 2,
            "sanitize" => 1,
            _ => 0
        };

        return Rank(next) >= Rank(current) ? next : current;
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

    private static List<OllamaTool>? StripRequiredToolSchemas(List<OllamaTool>? tools)
    {
        if (tools is null || tools.Count == 0)
            return tools;

        return tools
            .Select(t => new OllamaTool(
                t.Type,
                new OllamaFunction(
                    t.Function.Name,
                    t.Function.Description,
                    McpInputSchemaSanitizer.StripRequired(t.Function.Parameters))))
            .ToList();
    }

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
