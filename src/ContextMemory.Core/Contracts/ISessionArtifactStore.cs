namespace ContextMemory.Core.Contracts;

/// <summary>
/// Session-scoped blobs for dynamic context discovery (Cursor-style tool outputs as "files").
/// Kept outside the compiled wiki so hoist/index never touch them.
/// </summary>
public interface ISessionArtifactStore
{
    Task WriteAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        string content,
        CancellationToken cancellationToken = default);

    Task<string?> ReadAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        CancellationToken cancellationToken = default);

    Task<string?> TailAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        int maxChars,
        CancellationToken cancellationToken = default);
}
