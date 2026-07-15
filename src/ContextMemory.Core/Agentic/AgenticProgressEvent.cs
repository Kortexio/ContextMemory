namespace ContextMemory.Core.Agentic;

public enum AgenticProgressPhase
{
    Started,
    LlmRequest,
    ToolStarted,
    ToolCompleted,
    Validating,
    ValidationRejected,
    AwaitingConfirmation,
    ConfirmationReceived,
    Completed,
    TimedOut,
    MaxIterations
}

public sealed class AgenticProgressEvent
{
    public required AgenticProgressPhase Phase { get; init; }
    public int? Iteration { get; init; }
    public string? ToolName { get; init; }
    public string? Detail { get; init; }
    public AgentExecutionStep? Step { get; init; }
}

public sealed class AgenticOrchestratorEvent
{
    public AgenticProgressEvent? Progress { get; init; }
    public AgentResult? Result { get; init; }

    public static AgenticOrchestratorEvent FromProgress(AgenticProgressEvent progress) =>
        new() { Progress = progress };

    public static AgenticOrchestratorEvent FromResult(AgentResult result) =>
        new() { Result = result };
}
