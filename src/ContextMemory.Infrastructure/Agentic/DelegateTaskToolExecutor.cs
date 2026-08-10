using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

/// <summary>Depth-1 subagent: isolated child session, result returned as artifact + summary.</summary>
public sealed class DelegateTaskToolExecutor : ISessionScopedToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionArtifactStore _artifacts;
    private readonly ILogger<DelegateTaskToolExecutor> _logger;

    public DelegateTaskToolExecutor(
        IServiceScopeFactory scopeFactory,
        ISessionArtifactStore artifacts,
        ILogger<DelegateTaskToolExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        string.Equals(toolName, SessionDiscoveryTools.DelegateTask, StringComparison.OrdinalIgnoreCase);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId.Contains(":sub:", StringComparison.OrdinalIgnoreCase)
            || sessionId.Contains("/sub/", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolExecutionResult
            {
                Output = "delegate_task refused: subagents cannot nest (depth limit = 1).",
                ExitCode = 1
            };
        }

        string task;
        var maxIterations = 4;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            task = root.TryGetProperty("task", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("maxIterations", out var m) && m.TryGetInt32(out var n) && n > 0)
                maxIterations = Math.Min(n, 8);
        }
        catch
        {
            return new ToolExecutionResult
            {
                Output = "Invalid delegate_task arguments. Expected { \"task\": \"...\" }.",
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return new ToolExecutionResult
            {
                Output = "delegate_task requires a non-empty task.",
                ExitCode = 1
            };
        }

        var childSessionId = $"{sessionId}:sub:{Guid.NewGuid():N}"[..Math.Min(120, sessionId.Length + 40)];
        report?.Invoke(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.SubagentStarted,
            ToolName = SessionDiscoveryTools.DelegateTask,
            Detail = $"childSessionId={childSessionId}; task={task}"
        });

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var toolRegistry = scope.ServiceProvider.GetRequiredService<IAgenticToolRegistry>();
            var loopRunner = scope.ServiceProvider.GetRequiredService<IAgentLoopRunner>();

            var childMax = Math.Min(maxIterations, Math.Max(1, runtimeConfig.Agentic.MaxIterations));
            var childConfig = runtimeConfig with
            {
                Agentic = runtimeConfig.Agentic with
                {
                    Guardrails = runtimeConfig.Agentic.Guardrails with { MaxIterations = childMax }
                }
            };

            var tools = (await toolRegistry
                    .BuildToolsAsync(childConfig, task, recentToolNames: null, cancellationToken)
                    .ConfigureAwait(false))
                .Where(t => !string.Equals(t.Function.Name, SessionDiscoveryTools.DelegateTask, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var toolNamesSummary = await toolRegistry
                .BuildToolNamesSummaryAsync(childConfig, task, recentToolNames: null, cancellationToken)
                .ConfigureAwait(false);
            var mcpServers = toolRegistry.BuildMcpServers(childConfig);

            var system = AgenticSystemPromptBuilder.Build(childConfig, toolNamesSummary);
            var messages = new List<OllamaMessage>();
            if (!string.IsNullOrWhiteSpace(system))
                messages.Add(new OllamaMessage { Role = "system", Content = system });
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = "You are a focused subagent. Complete this task and stop:\n\n" + task.Trim()
            });

            var enriched = new OllamaRequest
            {
                Model = runtimeConfig.LlmModel,
                Messages = messages,
                Stream = false,
                Tools = tools
            };

            Action<AgenticProgressEvent>? childReport = evt =>
            {
                report?.Invoke(new AgenticProgressEvent
                {
                    Phase = evt.Phase,
                    Iteration = evt.Iteration,
                    ToolName = evt.ToolName,
                    ArtifactId = evt.ArtifactId,
                    Step = evt.Step,
                    Detail = $"[sub {childSessionId}] {evt.Detail}"
                });
            };

            var result = await loopRunner.RunAsync(
                new AgentLoopRequest
                {
                    AppId = appId,
                    UserId = userId,
                    SessionId = childSessionId,
                    EnrichedRequest = enriched,
                    RuntimeConfig = childConfig,
                    Messages = messages,
                    Steps = [],
                    Tools = tools,
                    McpServers = mcpServers,
                    StartIteration = 1,
                    Report = childReport
                },
                cancellationToken).ConfigureAwait(false);

            var artifactId = $"subagent:{childSessionId}";
            var transcript =
                $"# Subagent result\n\nchildSessionId={childSessionId}\n"
                + $"success={result.Success}\niterations={result.Iterations}\n\n"
                + $"## Answer\n{result.FinalAnswer}\n\n"
                + $"## Steps\n"
                + string.Join('\n', result.Steps.Select(s =>
                    $"- iter={s.Iteration} tool={s.ToolName} ok={s.Success} exit={s.ExitCode}"));

            await _artifacts
                .WriteAsync(appId, userId, sessionId, artifactId, transcript, cancellationToken)
                .ConfigureAwait(false);

            report?.Invoke(new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.SubagentCompleted,
                ToolName = SessionDiscoveryTools.DelegateTask,
                ArtifactId = artifactId,
                Detail = $"childSessionId={childSessionId}; artifactId={artifactId}"
            });

            var summary = result.FinalAnswer ?? string.Empty;
            if (summary.Length > 1200)
                summary = summary[..1200] + "…";

            return new ToolExecutionResult
            {
                Output =
                    $"Subagent completed (session={childSessionId}).\n"
                    + $"artifactId={artifactId}\n\n"
                    + summary,
                ExitCode = result.Success || !string.IsNullOrWhiteSpace(result.FinalAnswer) ? 0 : 1,
                Summary = summary,
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifactId"] = artifactId,
                    ["childSessionId"] = childSessionId
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "delegate_task failed for {AppId}/{SessionId}", appId, sessionId);
            report?.Invoke(new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.SubagentCompleted,
                ToolName = SessionDiscoveryTools.DelegateTask,
                Detail = $"failed: {ex.Message}"
            });
            return new ToolExecutionResult
            {
                Output = $"delegate_task failed: {ex.Message}",
                ExitCode = 1
            };
        }
    }
}
