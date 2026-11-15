using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Persists and loads agent run snapshots for pause / HITL / process restart (CM-4).
/// </summary>
public interface IAgentRunStore
{
    Task SaveAsync(AgentRunSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<AgentRunSnapshot?> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        string runId,
        CancellationToken cancellationToken = default);
}
