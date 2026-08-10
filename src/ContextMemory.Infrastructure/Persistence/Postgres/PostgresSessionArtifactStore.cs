using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Infrastructure.Session;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

/// <summary>
/// Stores artifacts inside the session JSON blob (no schema migration).
/// </summary>
public sealed class PostgresSessionArtifactStore : ISessionArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public PostgresSessionArtifactStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task WriteAsync(
        string appId,
        string userId,
        string sessionId,
        string artifactId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var id = FileSessionArtifactStore.SanitizeId(artifactId);
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.SessionRecords
                .FirstOrDefaultAsync(
                    s => s.AppId == appId && s.UserId == userId && s.SessionId == sessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            var record = Deserialize(entity?.DataJson);
            record.Artifacts[id] = content ?? string.Empty;
            var json = JsonSerializer.Serialize(record, JsonOptions);

            if (entity is null)
            {
                db.SessionRecords.Add(new SessionRecordEntity
                {
                    AppId = appId,
                    UserId = userId,
                    SessionId = sessionId,
                    DataJson = json,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                entity.DataJson = json;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        var id = FileSessionArtifactStore.SanitizeId(artifactId);
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.SessionRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.AppId == appId && s.UserId == userId && s.SessionId == sessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
                return null;

            var record = Deserialize(entity.DataJson);
            return record.Artifacts.TryGetValue(id, out var content) ? content : null;
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

    private static SessionPersistenceRecord Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SessionPersistenceRecord();

        try
        {
            return JsonSerializer.Deserialize<SessionPersistenceRecord>(json, JsonOptions)
                   ?? new SessionPersistenceRecord();
        }
        catch
        {
            return new SessionPersistenceRecord();
        }
    }

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
}
