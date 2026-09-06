using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Subagent;

public enum SubagentRole
{
    Researcher,
    Analyst,
    Reviewer,
    General,
    Coder,
    Tester
}

public enum SubagentSharedMemoryMode
{
    None,
    ReadOnly,
    ReadWrite
}

public sealed class SubagentSharedMemoryPolicy
{
    public SubagentSharedMemoryMode Mode { get; init; } = SubagentSharedMemoryMode.None;
}

public sealed class SubagentSpec
{
    public SubagentRole Role { get; init; } = SubagentRole.General;
    public int MaxDepth { get; init; } = 2;
    public int MaxIterations { get; init; } = 4;
    public string? ModelHint { get; init; }
    public SubagentSharedMemoryMode SharedMemoryMode { get; init; } = SubagentSharedMemoryMode.None;
    public int BudgetTokens { get; init; }
}

public sealed class SubagentResult
{
    public required string Summary { get; init; }
    public string? ArtifactId { get; init; }
    public bool Success { get; init; }
    public IReadOnlyList<AgentExecutionStep> Steps { get; init; } = [];
    public required string ChildSessionId { get; init; }
}

/// <summary>Parent agent context passed into subagent orchestration.</summary>
public sealed class SubagentParentContext
{
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public required AppRuntimeConfig RuntimeConfig { get; init; }
    public Action<AgenticProgressEvent>? Report { get; init; }
}

public sealed class SubagentParallelAggregate
{
    public required string AggregatedSummary { get; init; }
    public IReadOnlyList<SubagentResult> Results { get; init; } = [];
    public bool Success { get; init; }
}
