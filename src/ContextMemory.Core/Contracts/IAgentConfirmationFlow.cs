using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Human-in-the-loop confirmation for destructive agentic actions.
/// </summary>
public interface IAgentConfirmationFlow
{
    Task<AgentConfirmationFlowResult> TryResolvePendingAsync(
        string appId,
        string userId,
        string sessionId,
        string? lastUserMessage,
        Action<AgenticProgressEvent>? report,
        CancellationToken cancellationToken = default);
}

public sealed class AgentConfirmationFlowResult
{
    public bool IsResolved { get; init; }
    public AgentResult? Result { get; init; }
    public List<OllamaMessage>? ResumeMessages { get; init; }
    public List<AgentExecutionStep>? ResumeSteps { get; init; }
    public AgenticPendingState? ConfirmedPending { get; init; }
    public int StartIteration { get; init; } = 1;

    public static AgentConfirmationFlowResult Continue() => new();

    public static AgentConfirmationFlowResult Resolved(AgentResult result) =>
        new() { IsResolved = true, Result = result };

    public static AgentConfirmationFlowResult ResumeFrom(
        AgenticPendingState pending,
        List<OllamaMessage> messages,
        List<AgentExecutionStep> steps) =>
        new()
        {
            ResumeMessages = messages,
            ResumeSteps = steps,
            ConfirmedPending = pending,
            StartIteration = pending.Iteration
        };
}
