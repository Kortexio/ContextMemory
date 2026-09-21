using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic.Subagent;

/// <summary>CM-6a: depth-bounded subagent runs with optional parallel aggregation.</summary>
public sealed class SubagentOrchestrator : ISubagentOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionArtifactStore _artifacts;
    private readonly ILogger<SubagentOrchestrator> _logger;

    public SubagentOrchestrator(
        IServiceScopeFactory scopeFactory,
        ISessionArtifactStore artifacts,
        ILogger<SubagentOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public async Task<SubagentResult> RunAsync(
        SubagentParentContext parent,
        SubagentSpec spec,
        string objective,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(objective))
        {
            return new SubagentResult
            {
                Summary = "Subagent refused: empty objective.",
                Success = false,
                ChildSessionId = string.Empty,
                Steps = []
            };
        }

        var maxDepth = ResolveMaxDepth(parent.RuntimeConfig, spec);
        var currentDepth = ISubagentOrchestrator.GetSessionDepth(parent.SessionId);
        if (currentDepth >= maxDepth)
        {
            return new SubagentResult
            {
                Summary =
                    $"Subagent refused: depth limit reached (current={currentDepth}, max={maxDepth}).",
                Success = false,
                ChildSessionId = string.Empty,
                Steps = []
            };
        }

        var childSessionId = BuildChildSessionId(parent.SessionId);
        parent.Report?.Invoke(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.SubagentStarted,
            ToolName = SessionDiscoveryTools.DelegateTask,
            Detail = $"childSessionId={childSessionId}; role={spec.Role}; task={objective}"
        });

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var toolRegistry = scope.ServiceProvider.GetRequiredService<IAgenticToolRegistry>();
            var loopRunner = scope.ServiceProvider.GetRequiredService<IAgentLoopRunner>();

            var childMax = Math.Min(
                Math.Max(1, spec.MaxIterations),
                Math.Max(1, parent.RuntimeConfig.Agentic.MaxIterations));
            childMax = Math.Min(childMax, 8);

            var allowFurtherDelegate = currentDepth + 1 < maxDepth;
            var childConfig = parent.RuntimeConfig with
            {
                LlmModel = string.IsNullOrWhiteSpace(spec.ModelHint)
                    ? parent.RuntimeConfig.LlmModel
                    : spec.ModelHint!.Trim(),
                Agentic = parent.RuntimeConfig.Agentic with
                {
                    Guardrails = parent.RuntimeConfig.Agentic.Guardrails with
                    {
                        MaxIterations = childMax
                    }
                }
            };

            var tools = (await toolRegistry
                    .BuildToolsAsync(childConfig, objective, recentToolNames: null, cancellationToken)
                    .ConfigureAwait(false))
                .Where(t =>
                    allowFurtherDelegate
                    || !string.Equals(
                        t.Function.Name,
                        SessionDiscoveryTools.DelegateTask,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var toolNamesSummary = await toolRegistry
                .BuildToolNamesSummaryAsync(childConfig, objective, recentToolNames: null, cancellationToken)
                .ConfigureAwait(false);
            var mcpServers = toolRegistry.BuildMcpServers(childConfig);

            var system = AgenticSystemPromptBuilder.Build(childConfig, toolNamesSummary);
            var roleHint = BuildRoleHint(spec);
            var memoryHint = BuildSharedMemoryHint(spec.SharedMemoryMode);
            var messages = new List<OllamaMessage>();
            if (!string.IsNullOrWhiteSpace(system))
                messages.Add(new OllamaMessage { Role = "system", Content = system });
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content =
                    $"You are a focused {spec.Role} subagent.{roleHint}{memoryHint}\n"
                    + "Complete this task and stop:\n\n"
                    + objective.Trim()
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
                parent.Report?.Invoke(new AgenticProgressEvent
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
                    AppId = parent.AppId,
                    UserId = parent.UserId,
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
                .WriteAsync(
                    parent.AppId,
                    parent.UserId,
                    parent.SessionId,
                    artifactId,
                    transcript,
                    cancellationToken)
                .ConfigureAwait(false);

            parent.Report?.Invoke(new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.SubagentCompleted,
                ToolName = SessionDiscoveryTools.DelegateTask,
                ArtifactId = artifactId,
                Detail = $"childSessionId={childSessionId}; artifactId={artifactId}"
            });

            var summary = result.FinalAnswer ?? string.Empty;
            if (summary.Length > 1200)
                summary = summary[..1200] + "…";

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
            _logger.LogWarning(
                ex,
                "Subagent failed for {AppId}/{SessionId}",
                parent.AppId,
                parent.SessionId);
            parent.Report?.Invoke(new AgenticProgressEvent
            {
                Phase = AgenticProgressPhase.SubagentCompleted,
                ToolName = SessionDiscoveryTools.DelegateTask,
                Detail = $"failed: {ex.Message}"
            });
            return new SubagentResult
            {
                Summary = $"Subagent failed: {ex.Message}",
                Success = false,
                ChildSessionId = childSessionId,
                Steps = []
            };
        }
    }

    public async Task<SubagentParallelAggregate> RunParallelAsync(
        SubagentParentContext parent,
        IReadOnlyList<(SubagentSpec Spec, string Objective)> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(tasks);

        if (tasks.Count == 0)
        {
            return new SubagentParallelAggregate
            {
                AggregatedSummary = "No parallel subagent tasks provided.",
                Results = [],
                Success = false
            };
        }

        var maxParallel = parent.RuntimeConfig.Agentic.Guardrails.MaxParallelSubagents;
        if (maxParallel <= 0)
            maxParallel = 3;

        var capped = tasks.Take(maxParallel).ToList();
        var runs = capped.Select(t => RunAsync(parent, t.Spec, t.Objective, cancellationToken));
        var results = await Task.WhenAll(runs).ConfigureAwait(false);

        var md = AggregateMarkdown(results);
        return new SubagentParallelAggregate
        {
            AggregatedSummary = md,
            Results = results,
            Success = results.All(r => r.Success)
        };
    }

    internal static string AggregateMarkdown(IReadOnlyList<SubagentResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Parallel subagent results");
        sb.AppendLine();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## Task {i + 1}");
            sb.AppendLine($"- success: {r.Success}");
            sb.AppendLine($"- childSessionId: {r.ChildSessionId}");
            if (!string.IsNullOrWhiteSpace(r.ArtifactId))
                sb.AppendLine($"- artifactId: {r.ArtifactId}");
            sb.AppendLine();
            sb.AppendLine(r.Summary);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    internal static int ResolveMaxDepth(AppRuntimeConfig runtimeConfig, SubagentSpec spec)
    {
        var fromConfig = runtimeConfig.Agentic.Guardrails.MaxSubagentDepth;
        if (fromConfig <= 0)
            fromConfig = 2;
        var fromSpec = spec.MaxDepth > 0 ? spec.MaxDepth : fromConfig;
        return Math.Min(fromSpec, fromConfig);
    }

    internal static string BuildChildSessionId(string parentSessionId)
    {
        var raw = $"{parentSessionId}:sub:{Guid.NewGuid():N}";
        return raw[..Math.Min(120, parentSessionId.Length + 40)];
    }

    private static string BuildRoleHint(SubagentSpec spec) =>
        spec.Role switch
        {
            SubagentRole.Researcher => " Prioritize gathering and citing evidence.",
            SubagentRole.Analyst => " Prioritize structured analysis and trade-offs.",
            SubagentRole.Reviewer => " Prioritize critique, risks, and gaps.",
            SubagentRole.Coder => " Prioritize concrete implementation steps.",
            SubagentRole.Tester => " Prioritize verification, edge cases, and failure modes.",
            _ => string.Empty
        };

    private static string BuildSharedMemoryHint(SubagentSharedMemoryMode mode) =>
        mode switch
        {
            SubagentSharedMemoryMode.ReadOnly =>
                " Shared memory is read-only; do not mutate parent artifacts.",
            SubagentSharedMemoryMode.ReadWrite =>
                " Shared memory is read-write; prefer artifact writes for durable results.",
            _ => string.Empty
        };

    /// <summary>Parse optional role from tool JSON (case-insensitive name).</summary>
    public static bool TryParseRole(string? value, out SubagentRole role)
    {
        role = SubagentRole.General;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out role);
    }

    /// <summary>Build a <see cref="SubagentSpec"/> from delegate_task JSON args.</summary>
    public static SubagentSpec SpecFromToolArgs(JsonElement root, AppRuntimeConfig runtimeConfig)
    {
        var maxDepth = runtimeConfig.Agentic.Guardrails.MaxSubagentDepth;
        if (maxDepth <= 0)
            maxDepth = 2;

        var maxIterations = Math.Min(AgenticToolArguments.GetInt(root, "maxIterations", 4), 8);

        var depthArg = AgenticToolArguments.GetInt(root, "depth", 0);
        var maxDepthArg = AgenticToolArguments.GetInt(root, "maxDepth", 0);
        if (depthArg > 0)
            maxDepth = Math.Min(maxDepth, depthArg);
        else if (maxDepthArg > 0)
            maxDepth = Math.Min(maxDepth, maxDepthArg);

        var role = SubagentRole.General;
        TryParseRole(AgenticToolArguments.GetString(root, "role"), out role);

        var modelHint = AgenticToolArguments.GetString(root, "modelHint");

        var shared = SubagentSharedMemoryMode.None;
        if (Enum.TryParse(
                AgenticToolArguments.GetString(root, "sharedMemory"),
                ignoreCase: true,
                out SubagentSharedMemoryMode parsed))
        {
            shared = parsed;
        }

        var budget = AgenticToolArguments.GetInt(root, "budgetTokens", 0);

        return new SubagentSpec
        {
            Role = role,
            MaxDepth = maxDepth,
            MaxIterations = maxIterations,
            ModelHint = modelHint,
            SharedMemoryMode = shared,
            BudgetTokens = budget
        };
    }
}
