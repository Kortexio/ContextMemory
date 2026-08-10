using System.Diagnostics;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Agentic;

public sealed class AgentToolCallProcessor : IAgentToolCallProcessor
{
    private readonly IEnumerable<IToolExecutor> _toolExecutors;
    private readonly IEnumerable<ISessionScopedToolExecutor> _sessionToolExecutors;
    private readonly IAgenticPendingStore _pendingStore;
    private readonly ISessionStore _sessionStore;
    private readonly ISessionArtifactStore _artifactStore;
    private readonly ContextMemoryOptions _options;

    public AgentToolCallProcessor(
        IEnumerable<IToolExecutor> toolExecutors,
        IEnumerable<ISessionScopedToolExecutor> sessionToolExecutors,
        IAgenticPendingStore pendingStore,
        ISessionStore sessionStore,
        ISessionArtifactStore artifactStore,
        IOptions<ContextMemoryOptions> options)
    {
        _toolExecutors = toolExecutors;
        _sessionToolExecutors = sessionToolExecutors;
        _pendingStore = pendingStore;
        _sessionStore = sessionStore;
        _artifactStore = artifactStore;
        _options = options.Value;
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
            if (RequiresMcpConfirmation(toolCall, runtimeConfig))
            {
                var pending = new AgenticPendingState
                {
                    PendingId = Guid.NewGuid().ToString("N")[..12],
                    ToolName = toolCall.Function.Name,
                    Arguments = toolCall.Function.Arguments,
                    MatchedKeyword = "mcp-confirmation",
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

        var preHook = AgenticToolUseHooks.EvaluatePreToolUse(
            toolCall.Function.Name,
            toolCall.Function.Arguments,
            runtimeConfig);
        if (!preHook.Allowed)
        {
            var denied = new ToolExecutionResult
            {
                Output = preHook.Message ?? "Tool denied by PreToolUse hook.",
                ExitCode = 1
            };
            messages.Add(new OllamaMessage
            {
                Role = "tool",
                Content = AgenticToolObservationFormatter.Format(
                    toolCall.Function.Name, denied, runtimeConfig)
            });
            steps.Add(new AgentExecutionStep
            {
                Iteration = iteration,
                ToolName = toolCall.Function.Name,
                Arguments = toolCall.Function.Arguments,
                Output = denied.Output ?? string.Empty,
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = "PreToolUse denied"
            });
            return new AgentToolCallOutcome { Result = denied };
        }

        if (preHook.RequireConfirm && !skipConfirmation)
        {
            var pending = new AgenticPendingState
            {
                PendingId = Guid.NewGuid().ToString("N")[..12],
                ToolName = toolCall.Function.Name,
                Arguments = toolCall.Function.Arguments,
                MatchedKeyword = "pre-tool-use",
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

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ToolStarted,
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            Detail = toolCall.Function.Arguments
        });

        var sw = Stopwatch.StartNew();
        var toolResult = await ExecuteToolAsync(
                toolCall, appId, userId, sessionId, runtimeConfig, report, cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();

        toolResult = new ToolExecutionResult
        {
            Output = AgenticToolUseHooks.ApplyPostToolUse(
                toolCall.Function.Name, toolResult.Output ?? string.Empty, runtimeConfig),
            ExitCode = toolResult.ExitCode,
            Summary = toolResult.Summary,
            OutputTruncated = toolResult.OutputTruncated,
            Entities = toolResult.Entities
        };

        string? artifactId = null;
        var fullOutput = toolResult.Output ?? string.Empty;
        var maxObs = runtimeConfig.MaxToolObservationChars > 0
            ? runtimeConfig.MaxToolObservationChars
            : AgenticToolObservationFormatter.DefaultMaxObservationChars;
        var isSandbox = IsSandboxTool(toolCall.Function.Name);
        var shouldArchive = !SessionDiscoveryTools.IsDiscoveryTool(toolCall.Function.Name)
            && (isSandbox || fullOutput.Length > maxObs);

        if (shouldArchive && fullOutput.Length > 0)
        {
            artifactId = AgenticToolObservationFormatter.BuildArtifactId(toolCall.Function.Name, fullOutput);
            try
            {
                await _artifactStore
                    .WriteAsync(appId, userId, sessionId, artifactId, fullOutput, cancellationToken)
                    .ConfigureAwait(false);

                // Sandbox/terminal: always keep a short preview in the loop (Cursor-style).
                var previewChars = isSandbox
                    ? Math.Max(64, _options.SandboxObservationPreviewChars)
                    : maxObs;
                var preview = fullOutput.Length <= previewChars
                    ? fullOutput
                    : fullOutput[..previewChars];
                toolResult = new ToolExecutionResult
                {
                    Output = preview,
                    ExitCode = toolResult.ExitCode,
                    Summary = toolResult.Summary,
                    OutputTruncated = fullOutput.Length > previewChars,
                    Entities = MergeEntity(toolResult.Entities, "artifactId", artifactId)
                };
            }
            catch
            {
                artifactId = null;
            }
        }

        var step = new AgentExecutionStep
        {
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            Arguments = toolCall.Function.Arguments,
            Output = toolResult.Output ?? string.Empty,
            ExitCode = toolResult.ExitCode,
            Success = toolResult.Success,
            Duration = sw.Elapsed,
            Summary = toolResult.Summary,
            Entities = toolResult.Entities,
            OutputTruncated = toolResult.OutputTruncated || shouldArchive
        };
        steps.Add(step);

        Report(report, new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ToolCompleted,
            Iteration = iteration,
            ToolName = toolCall.Function.Name,
            ArtifactId = artifactId,
            Step = step
        });

        // Force pointer text for archived sandbox even when preview fits under Format's threshold.
        var observationConfig = isSandbox && artifactId is not null
            ? runtimeConfig with { MaxToolObservationChars = Math.Max(64, _options.SandboxObservationPreviewChars) }
            : runtimeConfig;

        messages.Add(new OllamaMessage
        {
            Role = "tool",
            Content = AgenticToolObservationFormatter.Format(
                toolCall.Function.Name, toolResult, observationConfig, artifactId)
        });

        return new AgentToolCallOutcome { Result = toolResult };
    }

    private async Task<ToolExecutionResult> ExecuteToolAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var sessionExecutor = _sessionToolExecutors
            .FirstOrDefault(e => e.CanExecute(toolCall.Function.Name, runtimeConfig));
        if (sessionExecutor is not null)
        {
            return await sessionExecutor
                .ExecuteAsync(toolCall, appId, userId, sessionId, runtimeConfig, report, cancellationToken)
                .ConfigureAwait(false);
        }

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

    private static Dictionary<string, string> MergeEntity(
        IReadOnlyDictionary<string, string>? entities,
        string key,
        string value)
    {
        var map = entities is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(entities, StringComparer.OrdinalIgnoreCase);
        map[key] = value;
        return map;
    }

    private static bool IsSandboxTool(string? toolName) =>
        string.Equals(toolName, AgenticToolRegistry.ShellExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.PythonExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.NodeExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.ContainerExecuteToolName, StringComparison.OrdinalIgnoreCase);

    private static bool RequiresMcpConfirmation(OllamaToolCall toolCall, AppRuntimeConfig runtimeConfig)
    {
        if (!Mcp.McpToolNaming.TryParseQualifiedName(toolCall.Function.Name, out var serverName, out var toolName))
            return false;

        var server = runtimeConfig.Agentic.Tools.Integrations.FirstOrDefault(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Mcp.McpToolNaming.SanitizeForCompare(i.Name), serverName, StringComparison.OrdinalIgnoreCase));
        if (server is null)
            return false;

        return server.RequiresConfirmation.Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase))
            || server.Capabilities.Any(c => string.Equals(c, "destructive", StringComparison.OrdinalIgnoreCase));
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
