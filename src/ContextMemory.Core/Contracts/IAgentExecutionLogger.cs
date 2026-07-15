using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Records agentic steps for observability and debugging.
/// </summary>
public interface IAgentExecutionLogger
{
    Task LogAsync(
        string appId,
        string userId,
        string sessionId,
        string? userObjective,
        AgentResult result,
        CancellationToken cancellationToken = default);
}
