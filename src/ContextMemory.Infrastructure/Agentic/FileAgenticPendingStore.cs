using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class FileAgenticPendingStore : IAgenticPendingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _sessionsRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FileAgenticPendingStore(IOptions<ContextMemoryOptions> options)
    {
        _sessionsRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "sessions");
        Directory.CreateDirectory(_sessionsRoot);
    }

    public async Task<AgenticPendingState?> TryLoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPendingPath(sessionId);
        if (!File.Exists(path))
            return null;

        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AgenticPendingState>(json, JsonOptions);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        string appId,
        string userId,
        string sessionId,
        AgenticPendingState state,
        CancellationToken cancellationToken = default)
    {
        var dir = GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);

        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(GetPendingPath(sessionId), json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPendingPath(sessionId);
        if (!File.Exists(path))
            return;

        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(path);
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPendingPath(string sessionId) =>
        Path.Combine(GetSessionDir(sessionId), "agentic-pending.json");

    private string GetSessionDir(string sessionId) =>
        Path.Combine(_sessionsRoot, SanitizeSessionId(sessionId));

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(SanitizeSessionId(sessionId), _ => new SemaphoreSlim(1, 1));

    private static string SanitizeSessionId(string sessionId)
    {
        var trimmed = sessionId.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("sessionId is required.");

        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');

        return trimmed;
    }
}
