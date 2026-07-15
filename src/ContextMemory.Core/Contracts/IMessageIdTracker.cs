namespace ContextMemory.Core.Contracts;

/// <summary>
/// Tracks and assigns stable message identifiers for chat turns.
/// </summary>
public interface IMessageIdTracker
{
    string CreateAndTrack(string appId, string userId);
    bool TryGetLast(string appId, string userId, out string? messageId);
}
