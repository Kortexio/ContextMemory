using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Dispatches tool calls to registered executors for a tenant.
/// </summary>
public interface IAgentToolCallProcessor
{
    Task<AgentToolCallOutcome> ProcessAsync(
        AgentToolCallContext context,
        CancellationToken cancellationToken = default);
}

public sealed class AgentToolCallOutcome
{
    public AgentResult? AwaitingConfirmation { get; init; }
    public ToolExecutionResult? Result { get; init; }
}
