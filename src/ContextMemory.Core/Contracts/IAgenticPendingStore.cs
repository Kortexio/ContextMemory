using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Persists pending HITL confirmation state per session.
/// </summary>
public interface IAgenticPendingStore
{
    Task<AgenticPendingState?> TryLoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string appId,
        string userId,
        string sessionId,
        AgenticPendingState state,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
