using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Persists session messages and wiki updates per app/user/session.
/// </summary>
public interface ISessionStore
{
    Task<SessionSnapshot> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task EnsureInitializedAsync(
        string appId,
        string userId,
        string sessionId,
        string appSchema,
        CancellationToken cancellationToken = default);

    Task AppendMessagesAsync(
        string appId,
        string userId,
        string sessionId,
        IEnumerable<OllamaMessage> messages,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task ApplyWikiUpdateAsync(
        string appId,
        string userId,
        string sessionId,
        SessionWikiUpdate update,
        CancellationToken cancellationToken = default);

    Task<int> DeleteSessionsOlderThanAsync(
        string appId,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);

    Task<int> DeleteSessionsForUserAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default);
}
