namespace ContextMemory.Core.Agentic;

/// <summary>
/// Default allowed-transition table for the agent loop (CM-4).
/// </summary>
public sealed class AgentStateMachine : IAgentStateMachine
{
    public TransitionResult Transition(AgentLoopState current, AgentLoopEvent evt)
    {
        if (TryResolve(current, evt, out var next))
            return new TransitionResult(next, IsValid: true);

        return new TransitionResult(
            current,
            IsValid: false,
            Error: $"Illegal transition: {current} + {evt}");
    }

    public AgentLoopState TransitionOrThrow(AgentLoopState current, AgentLoopEvent evt)
    {
        var result = Transition(current, evt);
        if (!result.IsValid)
            throw new InvalidOperationException(result.Error ?? $"Illegal transition: {current} + {evt}");

        return result.State;
    }

    private static bool TryResolve(AgentLoopState current, AgentLoopEvent evt, out AgentLoopState next)
    {
        next = current;

        // Global terminal escapes from any non-terminal state.
        if (evt is AgentLoopEvent.Fail && !IsTerminal(current))
        {
            next = AgentLoopState.Failed;
            return true;
        }

        if (evt is AgentLoopEvent.Cancel && !IsTerminal(current))
        {
            next = AgentLoopState.Cancelled;
            return true;
        }

        next = (current, evt) switch
        {
            (AgentLoopState.Created, AgentLoopEvent.Start) => AgentLoopState.Executing,

            (AgentLoopState.Planning, AgentLoopEvent.Plan) => AgentLoopState.Planning,
            (AgentLoopState.Planning, AgentLoopEvent.Compact) => AgentLoopState.Compacting,
            (AgentLoopState.Planning, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Planning, AgentLoopEvent.Delegate) => AgentLoopState.Delegating,
            (AgentLoopState.Planning, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.Planning, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.Executing, AgentLoopEvent.Plan) => AgentLoopState.Planning,
            (AgentLoopState.Executing, AgentLoopEvent.Compact) => AgentLoopState.Compacting,
            (AgentLoopState.Executing, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Executing, AgentLoopEvent.ToolCall) => AgentLoopState.WaitingForTool,
            (AgentLoopState.Executing, AgentLoopEvent.Validate) => AgentLoopState.Validating,
            (AgentLoopState.Executing, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.Executing, AgentLoopEvent.Delegate) => AgentLoopState.Delegating,
            (AgentLoopState.Executing, AgentLoopEvent.Recover) => AgentLoopState.Recovering,
            (AgentLoopState.Executing, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.WaitingForTool, AgentLoopEvent.ToolCall) => AgentLoopState.WaitingForTool,
            (AgentLoopState.WaitingForTool, AgentLoopEvent.ToolResult) => AgentLoopState.Observing,
            (AgentLoopState.WaitingForTool, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.WaitingForTool, AgentLoopEvent.Recover) => AgentLoopState.Recovering,
            (AgentLoopState.WaitingForTool, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,

            (AgentLoopState.Observing, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Observing, AgentLoopEvent.ToolCall) => AgentLoopState.WaitingForTool,
            (AgentLoopState.Observing, AgentLoopEvent.Validate) => AgentLoopState.Validating,
            (AgentLoopState.Observing, AgentLoopEvent.Compact) => AgentLoopState.Compacting,
            (AgentLoopState.Observing, AgentLoopEvent.Plan) => AgentLoopState.Planning,
            (AgentLoopState.Observing, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.Observing, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.Validating, AgentLoopEvent.ValidationRejected) => AgentLoopState.Executing,
            (AgentLoopState.Validating, AgentLoopEvent.Complete) => AgentLoopState.Completed,
            (AgentLoopState.Validating, AgentLoopEvent.Recover) => AgentLoopState.Recovering,
            (AgentLoopState.Validating, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.Validating, AgentLoopEvent.Plan) => AgentLoopState.Planning,

            (AgentLoopState.Compacting, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Compacting, AgentLoopEvent.Compact) => AgentLoopState.Compacting,
            (AgentLoopState.Compacting, AgentLoopEvent.Plan) => AgentLoopState.Planning,
            (AgentLoopState.Compacting, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.Recovering, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Recovering, AgentLoopEvent.Compact) => AgentLoopState.Compacting,
            (AgentLoopState.Recovering, AgentLoopEvent.ToolCall) => AgentLoopState.WaitingForTool,
            (AgentLoopState.Recovering, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.Delegating, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.Delegating, AgentLoopEvent.ToolResult) => AgentLoopState.Observing,
            (AgentLoopState.Delegating, AgentLoopEvent.AwaitHuman) => AgentLoopState.WaitingForHuman,
            (AgentLoopState.Delegating, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            (AgentLoopState.WaitingForHuman, AgentLoopEvent.LlmRequest) => AgentLoopState.Executing,
            (AgentLoopState.WaitingForHuman, AgentLoopEvent.Plan) => AgentLoopState.Planning,
            (AgentLoopState.WaitingForHuman, AgentLoopEvent.Complete) => AgentLoopState.Completed,

            _ => current
        };

        return next != current || IsIdempotent(current, evt);
    }

    private static bool IsTerminal(AgentLoopState state) =>
        state is AgentLoopState.Completed or AgentLoopState.Failed or AgentLoopState.Cancelled;

    private static bool IsIdempotent(AgentLoopState state, AgentLoopEvent evt) =>
        (state, evt) switch
        {
            (AgentLoopState.Executing, AgentLoopEvent.LlmRequest) => true,
            (AgentLoopState.Planning, AgentLoopEvent.Plan) => true,
            (AgentLoopState.Compacting, AgentLoopEvent.Compact) => true,
            (AgentLoopState.WaitingForTool, AgentLoopEvent.ToolCall) => true,
            (AgentLoopState.Completed, AgentLoopEvent.Complete) => true,
            (AgentLoopState.Failed, AgentLoopEvent.Fail) => true,
            (AgentLoopState.Cancelled, AgentLoopEvent.Cancel) => true,
            _ => false
        };
}
