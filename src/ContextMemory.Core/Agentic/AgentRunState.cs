namespace ContextMemory.Core.Agentic;

/// <summary>
/// High-level agent run lifecycle (CM-1). Finer-grained loop states live in <see cref="AgentLoopState"/>.
/// </summary>
public enum AgentRunState
{
    Created = 0,
    Running = 1,
    AwaitingHuman = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>
/// Explicit agent loop state machine states (CM-4).
/// </summary>
public enum AgentLoopState
{
    Created = 0,
    Planning = 1,
    Executing = 2,
    WaitingForTool = 3,
    WaitingForHuman = 4,
    Observing = 5,
    Validating = 6,
    Recovering = 7,
    Delegating = 8,
    Compacting = 9,
    Completed = 10,
    Failed = 11,
    Cancelled = 12
}
