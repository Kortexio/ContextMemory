using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Agentic;

public sealed class AgentLoopRunner : IAgentLoopRunner
{
    private static readonly AgentRetryPolicy TransientLlmRetry = new() { MaxAttempts = 3, BaseDelayMs = 250 };

    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAgentValidator _validator;
    private readonly IAgentToolCallProcessor _toolCallProcessor;
    private readonly ISessionStore _sessionStore;
    private readonly IAgenticPendingStore _pendingStore;
    private readonly IAgentContextCompactor _contextCompactor;
    private readonly IAgentStateMachine _stateMachine;
    private readonly IMcpToolCatalog _mcpCatalog;
    private readonly ILogger<AgentLoopRunner> _logger;
    private readonly ContextMemoryOptions _options;

    public AgentLoopRunner(
        ILlmAdapterResolver adapterResolver,
        IAgentValidator validator,
        IAgentToolCallProcessor toolCallProcessor,
        ISessionStore sessionStore,
        IAgenticPendingStore pendingStore,
        IAgentContextCompactor contextCompactor,
        IAgentStateMachine stateMachine,
        IMcpToolCatalog mcpCatalog,
        ILogger<AgentLoopRunner> logger,
        IOptions<ContextMemoryOptions> options)
    {
        _adapterResolver = adapterResolver;
        _validator = validator;
        _toolCallProcessor = toolCallProcessor;
        _sessionStore = sessionStore;
        _pendingStore = pendingStore;
        _contextCompactor = contextCompactor;
        _stateMachine = stateMachine;
        _mcpCatalog = mcpCatalog;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<AgentResult> RunAsync(AgentLoopRequest request, CancellationToken cancellationToken = default)
    {
        var messages = request.Messages;
        EnsureUserMessagePresent(messages, request);
        var steps = request.Steps;
        var toolsList = request.Tools.ToList();
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

        var trace = AgentTrace.Start(
            request.AppId,
            request.UserId,
            request.SessionId,
            request.RuntimeConfig.LlmModel);
        var loopState = AgentLoopState.Created;
        loopState = ApplyTransition(loopState, AgentLoopEvent.Start, trace);

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
        var forceAnswerOnly = false;

        for (var iteration = request.StartIteration - 1; iteration < maxIterations; iteration++)
        {
            if (loopSw.Elapsed >= loopTimeout)
            {
                _logger.LogWarning(
                    "Agentic loop timed out for {AppId} after {ElapsedMs}ms ({Iterations} iterations)",
                    request.AppId,
                    loopSw.ElapsedMilliseconds,
                    iteration);

                loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
                trace.Complete(AgentRunState.Failed);

                var timeoutResult = AttachDiscovery(
                    BuildTimeoutResult(lastAnswer, steps, iteration, request.RuntimeConfig.DefaultLanguage),
                    messages, steps, staticPromptChars, compactionCount, llmCalls,
                    promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                    .WithTrace(trace);
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

            var toolsForRequest = forceAnswerOnly
                ? null
                : (toolsList.Count > 0 ? toolsList : null);
            if (forceAnswerOnly)
                ClientSideToolCalling.ClearCatalogFromSystemPrompt(messages);
            if (toolsForRequest is not null && capabilities.SanitizeSchemasAggressively)
            {
                toolsForRequest = SanitizeToolSchemas(toolsForRequest);
                schemaRepairLevel = MaxRepairLevel(schemaRepairLevel, "sanitize");
            }

            var useClientSideTools = !forceAnswerOnly
                && capabilities.PreferClientSideToolParsing
                && toolsForRequest is { Count: > 0 };
            if (useClientSideTools)
            {
                ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, toolsForRequest!);
            }

            var tokenBudget = SessionWikiSettings.ResolveAgentCompactionTokenBudget(
                request.RuntimeConfig,
                _options,
                request.EnrichedRequest.Options?.NumCtx);

            var compaction = await _contextCompactor
                .TryCompactAsync(
                    request.AppId,
                    request.UserId,
                    request.SessionId,
                    request.RuntimeConfig,
                    messages,
                    iteration + 1,
                    cancellationToken,
                    tokenBudgetOverride: tokenBudget)
                .ConfigureAwait(false);
            if (compaction is not null)
            {
                compactionCount++;
                loopState = ApplyTransition(loopState, AgentLoopEvent.Compact, trace);
                if (useClientSideTools)
                    ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, toolsForRequest!);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Compacting,
                    Iteration = iteration + 1,
                    ArtifactId = compaction.HistoryArtifactId,
                    Detail = $"Compacted ~{compaction.EstimatedTokensBefore} tokens → summary + historyArtifactId"
                });
            }

            AgentContextCompactor.ShrinkToBudget(messages, tokenBudget);

            loopState = ApplyTransition(loopState, AgentLoopEvent.LlmRequest, trace);
            Report(request.Report, new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.LlmRequest,
                Iteration = iteration + 1
            });

            var toolChoice = useClientSideTools
                ? null
                : ResolveToolChoice(capabilities, requireToolChoice, toolsForRequest, request.RuntimeConfig);

            // format=json fights native tool_calls — clear on agentic iterations with tools.
            var format = toolsForRequest is { Count: > 0 }
                ? null
                : request.EnrichedRequest.Format;

            var wireMessages = useClientSideTools
                ? ClientSideToolCalling.FlattenForClientSideWire(messages)
                : messages;

            var llmRequest = request.EnrichedRequest with
            {
                Messages = wireMessages,
                // Omit native tools for Qwen/Weak — Ollama XML parser 500s on format drift.
                Tools = useClientSideTools ? null : toolsForRequest,
                McpServers = useClientSideTools
                    ? null
                    : (request.McpServers.Count > 0 ? request.McpServers.ToList() : null),
                Stream = false,
                ToolChoice = toolChoice,
                Format = format
            };

            OllamaResponse response;
            var skipProsePromotion = false;
            try
            {
                response = await ChatWithTransientRetryAsync(adapter, llmRequest, request.AppId, cancellationToken)
                    .ConfigureAwait(false);
                llmCalls++;
            }
            catch (HttpRequestException ex) when (IsNativeToolCallParseError(ex) && toolsForRequest is { Count: > 0 })
            {
                _logger.LogWarning(
                    ex,
                    "Native tool-call parse failed for {AppId}; falling back to client-side tool parsing",
                    request.AppId);

                ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, toolsForRequest);
                var fallbackRequest = llmRequest with
                {
                    Messages = ClientSideToolCalling.FlattenForClientSideWire(messages),
                    Tools = null,
                    McpServers = null,
                    ToolChoice = null
                };
                response = await ChatWithTransientRetryAsync(adapter, fallbackRequest, request.AppId, cancellationToken)
                    .ConfigureAwait(false);
                llmCalls++;
                useClientSideTools = true;
            }
            catch (HttpRequestException ex) when (IsStrictChatTemplateError(ex))
            {
                _logger.LogError(
                    ex,
                    "LLM chat template rejected messages for {AppId} (Qwen/Bonsai-style Jinja). Ensure a user message exists and avoid dual system roles; consider patching the model TEMPLATE.",
                    request.AppId);
                loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
                trace.Complete(AgentRunState.Failed);
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

                loopState = ApplyTransition(loopState, AgentLoopEvent.Recover, trace);
                response = await ChatWithGraduatedRepairAsync(
                        adapter,
                        llmRequest,
                        request.AppId,
                        repairLevel => schemaRepairLevel = MaxRepairLevel(schemaRepairLevel, repairLevel),
                        () => llmCalls++,
                        cancellationToken)
                    .ConfigureAwait(false);
                loopState = ApplyTransition(loopState, AgentLoopEvent.LlmRequest, trace);
            }
            catch (HttpRequestException ex) when (LlmContextLengthError.IsMatch(ex))
            {
                var bodyPreview = TruncateForLog(ex.Message, 2000);
                var nCtx = LlmContextLengthError.TryGetContextSize(ex.Message)
                           ?? request.EnrichedRequest.Options?.NumCtx
                           ?? request.RuntimeConfig.LlmOptions?.NumCtx
                           ?? 4096;
                var promptTokens = LlmContextLengthError.TryGetPromptTokens(ex.Message)
                                   ?? TokenEstimator.Estimate(messages);
                var fitBudget = SessionWikiSettings.FitBudgetForContextWindow(nCtx);

                _logger.LogWarning(
                    ex,
                    "LLM context window exceeded for {AppId} (prompt≈{PromptTokens}, n_ctx={NCtx}); shrinking to {Budget} tokens and retrying. Body: {Body}",
                    request.AppId,
                    promptTokens,
                    nCtx,
                    fitBudget,
                    bodyPreview);

                loopState = ApplyTransition(loopState, AgentLoopEvent.Recover, trace);
                Report(request.Report, new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Compacting,
                    Iteration = iteration + 1,
                    Detail = $"Prompt {promptTokens} tokens > n_ctx {nCtx}; shrinking context"
                });

                try
                {
                    await _contextCompactor
                        .TryCompactAsync(
                            request.AppId,
                            request.UserId,
                            request.SessionId,
                            request.RuntimeConfig,
                            messages,
                            iteration + 1,
                            cancellationToken,
                            tokenBudgetOverride: fitBudget,
                            force: true)
                        .ConfigureAwait(false);
                }
                catch (Exception compactEx)
                {
                    _logger.LogDebug(compactEx, "Forced compaction after context overflow failed; shrinking inline");
                }

                if (useClientSideTools && toolsForRequest is { Count: > 0 })
                    ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, toolsForRequest);
                AgentContextCompactor.ShrinkToBudget(messages, fitBudget);
                compactionCount++;

                var recoveredRequest = llmRequest with
                {
                    Messages = useClientSideTools
                        ? ClientSideToolCalling.FlattenForClientSideWire(messages)
                        : messages.ToList()
                };

                try
                {
                    response = await adapter.ChatAsync(recoveredRequest, cancellationToken).ConfigureAwait(false);
                    llmCalls++;
                    loopState = ApplyTransition(loopState, AgentLoopEvent.LlmRequest, trace);
                }
                catch (HttpRequestException stillOverEx) when (LlmContextLengthError.IsMatch(stillOverEx))
                {
                    _logger.LogWarning(
                        stillOverEx,
                        "Context overflow persisted after shrink for {AppId}; retrying without tools",
                        request.AppId);

                    AgentContextCompactor.ShrinkToBudget(messages, Math.Max(512, fitBudget * 2 / 3));
                    var noToolsRequest = recoveredRequest with
                    {
                        Messages = ClientSideToolCalling.FlattenForClientSideWire(messages),
                        Tools = null,
                        McpServers = null,
                        ToolChoice = null
                    };

                    try
                    {
                        response = await adapter.ChatAsync(noToolsRequest, cancellationToken).ConfigureAwait(false);
                        llmCalls++;
                        useClientSideTools = true;
                        skipProsePromotion = true;
                        loopState = ApplyTransition(loopState, AgentLoopEvent.LlmRequest, trace);
                    }
                    catch (HttpRequestException finalEx) when (LlmContextLengthError.IsMatch(finalEx) || IsHttpBadRequest(finalEx))
                    {
                        _logger.LogWarning(
                            finalEx,
                            "LLM context recovery failed for {AppId}. Body: {Body}",
                            request.AppId,
                            TruncateForLog(finalEx.Message, 2000));

                        if (!string.IsNullOrWhiteSpace(lastAnswer))
                        {
                            loopState = ApplyTransition(loopState, AgentLoopEvent.Complete, trace);
                            trace.Complete(AgentRunState.Completed);
                            var partial = AttachDiscovery(
                                AgentResult.Succeeded(lastAnswer, steps, iteration + 1),
                                messages, steps, staticPromptChars, compactionCount, llmCalls,
                                promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                                .WithTrace(trace);
                            Report(request.Report, new AgenticProgressEvent
                            {
                                Phase = AgenticProgressPhase.Completed,
                                Iteration = iteration + 1,
                                Detail = "Completed with prior answer after context overflow"
                            });
                            return partial;
                        }

                        loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
                        trace.Complete(AgentRunState.Failed);
                        var failed = AttachDiscovery(
                            AgentResult.Failed(
                                AgenticMessages.ContextWindowExceeded(promptTokens, nCtx, request.RuntimeConfig.DefaultLanguage),
                                steps,
                                iteration + 1),
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                            .WithTrace(trace);
                        Report(request.Report, new AgenticProgressEvent
                        {
                            Phase = AgenticProgressPhase.Completed,
                            Iteration = iteration + 1,
                            Detail = $"Prompt exceeded n_ctx={nCtx}"
                        });
                        return failed;
                    }
                }
            }
            catch (HttpRequestException ex) when (IsHttpBadRequest(ex))
            {
                var bodyPreview = TruncateForLog(ex.Message, 2000);
                _logger.LogWarning(
                    ex,
                    "LLM returned HTTP 400 for {AppId}; retrying without tools/tool_calls. Body: {Body}",
                    request.AppId,
                    bodyPreview);

                loopState = ApplyTransition(loopState, AgentLoopEvent.Recover, trace);
                var recoveryRequest = llmRequest with
                {
                    Messages = ClientSideToolCalling.FlattenForClientSideWire(messages),
                    Tools = null,
                    McpServers = null,
                    ToolChoice = null,
                    Format = request.EnrichedRequest.Format
                };

                try
                {
                    response = await ChatWithTransientRetryAsync(adapter, recoveryRequest, request.AppId, cancellationToken)
                        .ConfigureAwait(false);
                    llmCalls++;
                    useClientSideTools = true;
                    skipProsePromotion = true;
                    loopState = ApplyTransition(loopState, AgentLoopEvent.LlmRequest, trace);
                }
                catch (HttpRequestException recoveryEx) when (IsHttpBadRequest(recoveryEx))
                {
                    _logger.LogWarning(
                        recoveryEx,
                        "LLM HTTP 400 recovery also failed for {AppId}. Body: {Body}",
                        request.AppId,
                        TruncateForLog(recoveryEx.Message, 2000));

                    if (!string.IsNullOrWhiteSpace(lastAnswer))
                    {
                        loopState = ApplyTransition(loopState, AgentLoopEvent.Complete, trace);
                        trace.Complete(AgentRunState.Completed);
                        var partial = AttachDiscovery(
                            AgentResult.Succeeded(lastAnswer, steps, iteration + 1),
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                            .WithTrace(trace);
                        Report(request.Report, new AgenticProgressEvent
                        {
                            Phase = AgenticProgressPhase.Completed,
                            Iteration = iteration + 1,
                            Detail = "Completed with prior answer after LLM HTTP 400"
                        });
                        return partial;
                    }

                    loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
                    trace.Complete(AgentRunState.Failed);
                    throw;
                }
            }

            var assistantMessage = response.Message;

            if (!forceAnswerOnly
                && !skipProsePromotion
                && (capabilities.EnableProseToolCallPromotion || useClientSideTools)
                && assistantMessage is not null
                && (assistantMessage.ToolCalls is null || assistantMessage.ToolCalls.Count == 0))
            {
                var rawPromoted = ProseToolCallParser.TryParse(
                    OllamaLlmText.GetMessageContent(assistantMessage));
                if (rawPromoted is { Count: > 0 })
                {
                    var maxPerTurn = LlmCapabilitiesResolver.ResolveMaxMcpTools(request.RuntimeConfig);
                    var promoted = ProseToolCallParser.FilterAgainstCatalog(
                        rawPromoted,
                        toolsForRequest,
                        maxPerTurn,
                        out var droppedUnknown,
                        out var droppedInvalidArgs,
                        out var droppedCapped);

                    if (droppedUnknown > 0 || droppedInvalidArgs > 0 || droppedCapped > 0)
                    {
                        _logger.LogWarning(
                            "Dropped prose tool call(s) for {AppId}: unknown={Unknown}, invalidArgs={InvalidArgs}, capped={Capped} (raw={Raw}, kept={Kept}, maxPerTurn={Max})",
                            request.AppId,
                            droppedUnknown,
                            droppedInvalidArgs,
                            droppedCapped,
                            rawPromoted.Count,
                            promoted?.Count ?? 0,
                            maxPerTurn);
                    }

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
            }

            // When client-side, keep history as flat chat (avoid feeding tool_calls back to Ollama).
            if (!forceAnswerOnly && assistantMessage?.ToolCalls is { Count: > 0 } toolCalls)
            {
                requireToolChoice = false;
                if (useClientSideTools)
                {
                    messages.Add(ClientSideToolCalling.FlattenForClientSideWire(
                    [
                        assistantMessage with { Content = string.Empty, ToolCalls = toolCalls.ToList() }
                    ])[0]);
                }
                else
                {
                    messages.Add(assistantMessage);
                }

                foreach (var toolCall in toolCalls)
                {
                    if (loopSw.Elapsed >= loopTimeout)
                    {
                        loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
                        trace.Complete(AgentRunState.Failed);
                        var timeoutResult = AttachDiscovery(
                            BuildTimeoutResult(lastAnswer, steps, iteration + 1, request.RuntimeConfig.DefaultLanguage),
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                            .WithTrace(trace);
                        Report(request.Report, new AgenticProgressEvent
                        {
                            Phase = AgenticProgressPhase.TimedOut,
                            Iteration = iteration + 1,
                            Detail = AgenticMessages.ToolTimeout(request.RuntimeConfig)
                        });
                        return timeoutResult;
                    }

                    loopState = ApplyTransition(loopState, AgentLoopEvent.ToolCall, trace);
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
                        loopState = ApplyTransition(loopState, AgentLoopEvent.AwaitHuman, trace);
                        trace.Complete(AgentRunState.AwaitingHuman);
                        return AttachDiscovery(
                            toolOutcome.AwaitingConfirmation,
                            messages, steps, staticPromptChars, compactionCount, llmCalls,
                            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                            .WithTrace(trace);
                    }

                    if (toolOutcome.Result is { Success: true }
                        && string.Equals(
                            toolCall.Function.Name,
                            SessionDiscoveryTools.ToolDescribe,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await TryPinMcpToolAfterDescribeAsync(
                                toolCall,
                                request.RuntimeConfig,
                                toolsList,
                                messages,
                                capabilities.PreferClientSideToolParsing,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    loopState = ApplyTransition(loopState, AgentLoopEvent.ToolResult, trace);
                }

                if (!forceAnswerOnly
                    && AgenticDuplicateToolCallGuard.ShouldForceAnswerAfterWikiBudget(steps))
                {
                    forceAnswerOnly = true;
                    requireToolChoice = false;
                    _logger.LogWarning(
                        "Forcing answer-only iteration for {AppId} after repeated wiki budget rejections",
                        request.AppId);
                    messages.Add(new OllamaMessage
                    {
                        Role = "user",
                        Content = AgenticDuplicateToolCallGuard.BuildForceAnswerNudge(
                            request.RuntimeConfig,
                            steps)
                    });
                }

                continue;
            }

            lastAnswer = OllamaLlmText.NormalizeAssistantContent(
                OllamaLlmText.GetMessageContent(assistantMessage));

            loopState = ApplyTransition(loopState, AgentLoopEvent.Validate, trace);
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
                loopState = ApplyTransition(loopState, AgentLoopEvent.Complete, trace);
                trace.Complete(AgentRunState.Completed);
                var success = AttachDiscovery(
                    AgentResult.Succeeded(lastAnswer, steps, iteration + 1),
                    messages, steps, staticPromptChars, compactionCount, llmCalls,
                    promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                    .WithTrace(trace);
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
            loopState = ApplyTransition(loopState, AgentLoopEvent.ValidationRejected, trace);

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
            loopState = ApplyTransition(loopState, AgentLoopEvent.AwaitHuman, trace);
            trace.Complete(AgentRunState.AwaitingHuman);
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
                promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
                .WithTrace(trace);
        }

        Report(request.Report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.MaxIterations,
            Detail = AgenticMessages.MaxIterationsReached(maxIterations, request.RuntimeConfig.DefaultLanguage)
        });

        loopState = ApplyTransition(loopState, AgentLoopEvent.Fail, trace);
        trace.Complete(AgentRunState.Failed);
        return AttachDiscovery(
            AgentResult.LimitReached(fallback, steps, maxIterations),
            messages, steps, staticPromptChars, compactionCount, llmCalls,
            promotedProseToolCalls, resolvedProfile, capabilities.HarnessMode.ToString(), schemaRepairLevel)
            .WithTrace(trace);
    }

    private async Task<OllamaResponse> ChatWithTransientRetryAsync(
        ILlmAdapter adapter,
        OllamaRequest llmRequest,
        string appId,
        CancellationToken cancellationToken)
    {
        Exception? lastTransient = null;
        for (var attempt = 1; attempt <= TransientLlmRetry.MaxAttempts; attempt++)
        {
            try
            {
                return await adapter.ChatAsync(llmRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                TransientLlmRetry.ShouldRetry(ex, attempt, TransientLlmRetry.MaxAttempts, cancellationToken)
                && ex is HttpRequestException httpEx
                && !IsSpecialCasedLlmHttpError(httpEx))
            {
                lastTransient = ex;
                _logger.LogWarning(
                    ex,
                    "Transient LLM HTTP failure for {AppId} (attempt {Attempt}/{Max}); retrying after {DelayMs}ms",
                    appId,
                    attempt,
                    TransientLlmRetry.MaxAttempts,
                    TransientLlmRetry.GetDelay(attempt).TotalMilliseconds);
                await Task.Delay(TransientLlmRetry.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastTransient ?? new InvalidOperationException("Transient LLM retry exhausted without capturing an exception.");
    }

    private static bool IsSpecialCasedLlmHttpError(HttpRequestException ex) =>
        IsOllamaToolXmlParseError(ex)
        || IsStrictChatTemplateError(ex)
        || IsLlmGrammarError(ex)
        || IsHttpBadRequest(ex);

    private AgentLoopState ApplyTransition(AgentLoopState current, AgentLoopEvent evt, AgentTrace trace)
    {
        var result = _stateMachine.Transition(current, evt);
        if (!result.IsValid)
        {
            _logger.LogDebug(
                "Agent state transition skipped: {Current} + {Event} ({Error})",
                current,
                evt,
                result.Error);
            return current;
        }

        trace.LoopState = result.State;
        return result.State;
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

    private static bool IsNativeToolCallParseError(HttpRequestException ex)
    {
        // Backend-agnostic: any native tool_calls wire failure → fall back to client-side catalog.
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("XML syntax error", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("element <function> closed by", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("qwen tool call parsing failed", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("Failed to parse tool call arguments as JSON", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("parse tool call arguments", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("json.exception.parse_error", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("tool call arguments as JSON", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOllamaToolXmlParseError(HttpRequestException ex) =>
        IsNativeToolCallParseError(ex);

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

    private static bool IsHttpBadRequest(HttpRequestException ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.BadRequest
        || (ex.Message?.Contains("\"code\":400", StringComparison.Ordinal) == true)
        || (ex.Message?.Contains("status code 400", StringComparison.OrdinalIgnoreCase) == true);

    private static string TruncateForLog(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "…";
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

    private async Task TryPinMcpToolAfterDescribeAsync(
        OllamaToolCall describeCall,
        AppRuntimeConfig runtimeConfig,
        List<OllamaTool> toolsList,
        List<OllamaMessage> messages,
        bool preferClientSideTools,
        CancellationToken cancellationToken)
    {
        string? toolName = null;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(describeCall.Function.Arguments)
                    ? "{}"
                    : describeCall.Function.Arguments);
            var root = doc.RootElement;
            toolName = TryGetJsonString(root, "toolName")
                       ?? TryGetJsonString(root, "name")
                       ?? TryGetJsonString(root, "tool");
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(toolName))
            return;

        toolName = toolName.Trim();
        if (toolsList.Any(t =>
                string.Equals(t.Function.Name, toolName, StringComparison.OrdinalIgnoreCase)))
            return;

        var catalog = await _mcpCatalog
            .GetAllToolsAsync(runtimeConfig, cancellationToken)
            .ConfigureAwait(false);
        var match = catalog.FirstOrDefault(t =>
            string.Equals(t.QualifiedName, toolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;

        var max = LlmCapabilitiesResolver.ResolveMaxMcpTools(runtimeConfig);
        var pinnedCount = toolsList.Count(t =>
            McpToolNaming.TryParseQualifiedName(t.Function.Name, out _, out _));
        if (pinnedCount >= max)
            return;

        toolsList.Add(McpPinnedToolFactory.Create(match, runtimeConfig));
        _logger.LogInformation(
            "Pinned MCP tool {Tool} into turn catalog for {AppId} after tool_describe",
            match.QualifiedName,
            runtimeConfig.AppId);

        if (preferClientSideTools && toolsList.Count > 0)
            ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, toolsList);
    }

    private static string? TryGetJsonString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

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
