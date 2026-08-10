using System.Collections.Concurrent;
using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Session;

public sealed class FileSessionArtifactStore : ISessionArtifactStore
{
    private readonly string _sessionsRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FileSessionArtifactStore(IOptions<ContextMemoryOptions> options)
    {
        _sessionsRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "sessions");
        Directory.CreateDirectory(_sessionsRoot);
    }

    public async Task WriteAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        string content,
        CancellationToken cancellationToken = default)
    {
        _ = appId;
        _ = userId;
        var id = SanitizeId(artifactId);
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = GetArtifactsDir(sessionId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, id + ".txt");
            await File.WriteAllTextAsync(path, content ?? string.Empty, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> ReadAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        _ = appId;
        _ = userId;
        var id = SanitizeId(artifactId);
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(GetArtifactsDir(sessionId), id + ".txt");
            if (!File.Exists(path))
                return null;
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> TailAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        int maxChars,
        CancellationToken cancellationToken = default)
    {
        var full = await ReadAsync(appId, userId, sessionId, artifactId, cancellationToken).ConfigureAwait(false);
        if (full is null)
            return null;
        if (maxChars <= 0 || full.Length <= maxChars)
            return full;
        return full[^maxChars..];
    }

    private string GetArtifactsDir(string sessionId) =>
        Path.Combine(_sessionsRoot, sessionId, "pages", "_artifacts");

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    internal static string SanitizeId(string artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("artifactId is required.", nameof(artifactId));

        var sb = new StringBuilder(artifactId.Length);
        foreach (var ch in artifactId.Trim())
        {
            // Windows forbids ':' in file names — map separators to '_'.
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        var sanitized = sb.ToString();
        if (sanitized.Length > 180)
            sanitized = sanitized[..180];
        return sanitized;
    }
}
