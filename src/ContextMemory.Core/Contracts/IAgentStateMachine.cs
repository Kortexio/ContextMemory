using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

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
