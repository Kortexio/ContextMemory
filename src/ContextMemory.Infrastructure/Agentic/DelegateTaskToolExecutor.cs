using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Subagent;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

/// <summary>Subagent tool: isolated child session(s), result returned as artifact + summary.</summary>
public sealed class DelegateTaskToolExecutor : ISessionScopedToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionArtifactStore _artifacts;
    private readonly ILogger<DelegateTaskToolExecutor> _logger;
    private readonly ISubagentOrchestrator? _orchestrator;

    public DelegateTaskToolExecutor(
        IServiceScopeFactory scopeFactory,
        ISessionArtifactStore artifacts,
        ILogger<DelegateTaskToolExecutor> logger,
        ISubagentOrchestrator? orchestrator = null)
    {
        _scopeFactory = scopeFactory;
        _artifacts = artifacts;
        _logger = logger;
        _orchestrator = orchestrator;
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
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            root = doc.RootElement.Clone();
        }
        catch
        {
            return new ToolExecutionResult
            {
                Output = "Invalid delegate_task arguments. Expected { \"task\": \"...\" }.",
                ExitCode = 1
            };
        }

        var maxDepth = runtimeConfig.Agentic.Guardrails.MaxSubagentDepth;
        if (maxDepth <= 0)
            maxDepth = 2;

        var currentDepth = ISubagentOrchestrator.GetSessionDepth(sessionId);
        if (currentDepth >= maxDepth)
        {
            return new ToolExecutionResult
            {
                Output =
                    $"delegate_task refused: subagents cannot nest further "
                    + $"(current depth={currentDepth}, max={maxDepth}).",
                ExitCode = 1
            };
        }

        var parallel = false;
        if (root.TryGetProperty("parallel", out var p))
        {
            parallel = p.ValueKind == JsonValueKind.True
                       || (p.ValueKind == JsonValueKind.String
                           && bool.TryParse(p.GetString(), out var pb)
                           && pb);
        }

        if (parallel && root.TryGetProperty("tasks", out var tasksEl) && tasksEl.ValueKind == JsonValueKind.Array)
            return await ExecuteParallelAsync(
                appId, userId, sessionId, runtimeConfig, root, tasksEl, report, cancellationToken)
                .ConfigureAwait(false);

        return await ExecuteSingleAsync(
                appId, userId, sessionId, runtimeConfig, root, report, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ToolExecutionResult> ExecuteParallelAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        JsonElement root,
        JsonElement tasksEl,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var list = new List<(SubagentSpec Spec, string Objective)>();
        foreach (var item in tasksEl.EnumerateArray())
        {
            var taskText = item.TryGetProperty("task", out var t)
                ? t.GetString() ?? string.Empty
                : item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(taskText))
                continue;

            var spec = item.ValueKind == JsonValueKind.Object
                ? SubagentOrchestrator.SpecFromToolArgs(item, runtimeConfig)
                : SubagentOrchestrator.SpecFromToolArgs(root, runtimeConfig);
            list.Add((spec, taskText.Trim()));
        }

        if (list.Count == 0)
        {
            return new ToolExecutionResult
            {
                Output = "delegate_task parallel requires a non-empty tasks array.",
                ExitCode = 1
            };
        }

        var parent = new SubagentParentContext
        {
            AppId = appId,
            UserId = userId,
            SessionId = sessionId,
            RuntimeConfig = runtimeConfig,
            Report = report
        };

        if (_orchestrator is not null)
        {
            var aggregate = await _orchestrator
                .RunParallelAsync(parent, list, cancellationToken)
                .ConfigureAwait(false);

            var firstOk = aggregate.Results.FirstOrDefault(r => r.Success);
            return new ToolExecutionResult
            {
                Output = aggregate.AggregatedSummary,
                ExitCode = aggregate.Success ? 0 : 1,
                Summary = Truncate(aggregate.AggregatedSummary, 1200),
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["parallel"] = "true",
                    ["count"] = aggregate.Results.Count.ToString(),
                    ["artifactId"] = firstOk?.ArtifactId ?? string.Empty,
                    ["childSessionId"] = firstOk?.ChildSessionId ?? string.Empty
                }
            };
        }

        // Fallback without orchestrator: sequential depth-checked runs via local loop.
        var results = new List<SubagentResult>();
        var maxParallel = runtimeConfig.Agentic.Guardrails.MaxParallelSubagents;
        if (maxParallel <= 0)
            maxParallel = 3;
        foreach (var (spec, objective) in list.Take(maxParallel))
        {
            var single = await RunLegacyChildAsync(
                    appId, userId, sessionId, runtimeConfig, spec, objective, report, cancellationToken)
                .ConfigureAwait(false);
            results.Add(single);
        }

        var md = SubagentOrchestrator.AggregateMarkdown(results);
        return new ToolExecutionResult
        {
            Output = md,
            ExitCode = results.All(r => r.Success) ? 0 : 1,
            Summary = Truncate(md, 1200)
        };
    }

    private async Task<ToolExecutionResult> ExecuteSingleAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        JsonElement root,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var task = root.TryGetProperty("task", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(task))
        {
            return new ToolExecutionResult
            {
                Output = "delegate_task requires a non-empty task.",
                ExitCode = 1
            };
        }

        var spec = SubagentOrchestrator.SpecFromToolArgs(root, runtimeConfig);
        var parent = new SubagentParentContext
        {
            AppId = appId,
            UserId = userId,
            SessionId = sessionId,
            RuntimeConfig = runtimeConfig,
            Report = report
        };

        if (_orchestrator is not null)
        {
            var result = await _orchestrator
                .RunAsync(parent, spec, task.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(result.ChildSessionId) && !result.Success)
            {
                return new ToolExecutionResult
                {
                    Output = result.Summary,
                    ExitCode = 1
                };
            }

            return new ToolExecutionResult
            {
                Output =
                    $"Subagent completed (session={result.ChildSessionId}).\n"
                    + $"artifactId={result.ArtifactId}\n\n"
                    + result.Summary,
                ExitCode = result.Success ? 0 : 1,
                Summary = result.Summary,
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifactId"] = result.ArtifactId ?? string.Empty,
                    ["childSessionId"] = result.ChildSessionId,
                    ["role"] = spec.Role.ToString()
                }
            };
        }

        var legacy = await RunLegacyChildAsync(
                appId, userId, sessionId, runtimeConfig, spec, task.Trim(), report, cancellationToken)
            .ConfigureAwait(false);

        return new ToolExecutionResult
        {
            Output =
                $"Subagent completed (session={legacy.ChildSessionId}).\n"
                + $"artifactId={legacy.ArtifactId}\n\n"
                + legacy.Summary,
            ExitCode = legacy.Success ? 0 : 1,
            Summary = legacy.Summary,
            Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["artifactId"] = legacy.ArtifactId ?? string.Empty,
                ["childSessionId"] = legacy.ChildSessionId,
                ["role"] = spec.Role.ToString()
            }
        };
    }

    private async Task<SubagentResult> RunLegacyChildAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        SubagentSpec spec,
        string task,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken)
    {
        var maxDepth = SubagentOrchestrator.ResolveMaxDepth(runtimeConfig, spec);
        var currentDepth = ISubagentOrchestrator.GetSessionDepth(sessionId);
        if (currentDepth >= maxDepth)
        {
            return new SubagentResult
            {
                Summary =
                    $"delegate_task refused: depth limit reached (current={currentDepth}, max={maxDepth}).",
                Success = false,
                ChildSessionId = string.Empty,
                Steps = []
            };
        }

        var childSessionId = SubagentOrchestrator.BuildChildSessionId(sessionId);
        report?.Invoke(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.SubagentStarted,
            ToolName = SessionDiscoveryTools.DelegateTask,
            Detail = $"childSessionId={childSessionId}; role={spec.Role}; task={task}"
        });

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var toolRegistry = scope.ServiceProvider.GetRequiredService<IAgenticToolRegistry>();
            var loopRunner = scope.ServiceProvider.GetRequiredService<IAgentLoopRunner>();

            var childMax = Math.Min(Math.Max(1, spec.MaxIterations), Math.Max(1, runtimeConfig.Agentic.MaxIterations));
            childMax = Math.Min(childMax, 8);
            var allowFurtherDelegate = currentDepth + 1 < maxDepth;
            var childConfig = runtimeConfig with
            {
                LlmModel = string.IsNullOrWhiteSpace(spec.ModelHint)
                    ? runtimeConfig.LlmModel
                    : spec.ModelHint!.Trim(),
                Agentic = runtimeConfig.Agentic with
                {
                    Guardrails = runtimeConfig.Agentic.Guardrails with { MaxIterations = childMax }
                }
            };

            var tools = (await toolRegistry
                    .BuildToolsAsync(childConfig, task, recentToolNames: null, cancellationToken)
                    .ConfigureAwait(false))
                .Where(t =>
                    allowFurtherDelegate
                    || !string.Equals(
                        t.Function.Name,
                        SessionDiscoveryTools.DelegateTask,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            var toolNamesSummary = await toolRegistry
                .BuildToolNamesSummaryAsync(childConfig, task, recentToolNames: null, cancellationToken)
                .ConfigureAwait(false);
            var mcpServers = toolRegistry.BuildMcpServers(childConfig);

            var system = Core.Agentic.Prompts.AgenticSystemPromptBuilder.Build(childConfig, toolNamesSummary);
            var messages = new List<OllamaMessage>();
            if (!string.IsNullOrWhiteSpace(system))
                messages.Add(new OllamaMessage { Role = "system", Content = system });
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = $"You are a focused {spec.Role} subagent. Complete this task and stop:\n\n" + task
            });

            var enriched = new OllamaRequest
            {
                Model = childConfig.LlmModel,
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
                + $"role={spec.Role}\n"
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

            var summary = Truncate(result.FinalAnswer ?? string.Empty, 1200);
            return new SubagentResult
            {
                Summary = summary,
                ArtifactId = artifactId,
                Success = result.Success || !string.IsNullOrWhiteSpace(result.FinalAnswer),
                Steps = result.Steps,
                ChildSessionId = childSessionId
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
            return new SubagentResult
            {
                Summary = $"delegate_task failed: {ex.Message}",
                Success = false,
                ChildSessionId = childSessionId,
                Steps = []
            };
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "…";
    }
}
