namespace ContextMemory.Core.Agentic;

/// <summary>
/// Events that drive the agent loop state machine (CM-4).
/// </summary>
public enum AgentLoopEvent
{
    Start = 0,
    Plan = 1,
    Compact = 2,
    LlmRequest = 3,
    ToolCall = 4,
    ToolResult = 5,
    Validate = 6,
    ValidationRejected = 7,
    AwaitHuman = 8,
    Delegate = 9,
    Recover = 10,
    Complete = 11,
    Fail = 12,
    Cancel = 13
}

/// <summary>
/// Result of a state transition attempt.
/// </summary>
/// <param name="State">Next state when valid; otherwise the unchanged current state.</param>
/// <param name="IsValid">Whether the transition was allowed.</param>
/// <param name="Error">Human-readable reason when <see cref="IsValid"/> is false.</param>
public sealed record TransitionResult(AgentLoopState State, bool IsValid, string? Error = null);

/// <summary>
/// Explicit agent loop state machine (CM-4).
/// </summary>
public interface IAgentStateMachine
{
    /// <summary>
    /// Attempts to transition from <paramref name="current"/> given <paramref name="evt"/>.
    /// Illegal transitions return the current state with <see cref="TransitionResult.IsValid"/> = false.
    /// </summary>
    TransitionResult Transition(AgentLoopState current, AgentLoopEvent evt);

    /// <summary>
    /// Same as <see cref="Transition"/> but throws <see cref="InvalidOperationException"/> on illegal transitions.
    /// </summary>
    AgentLoopState TransitionOrThrow(AgentLoopState current, AgentLoopEvent evt);
}
