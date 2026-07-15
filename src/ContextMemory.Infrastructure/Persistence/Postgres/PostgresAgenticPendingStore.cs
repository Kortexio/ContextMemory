using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresAgenticPendingStore : IAgenticPendingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public PostgresAgenticPendingStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<AgenticPendingState?> TryLoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.AgenticPendingRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
                return null;

            return JsonSerializer.Deserialize<AgenticPendingState>(entity.StateJson, JsonOptions);
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
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.AgenticPendingRecords
                .FirstOrDefaultAsync(
                    x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);

            var json = JsonSerializer.Serialize(state, JsonOptions);
            if (entity is null)
            {
                db.AgenticPendingRecords.Add(new AgenticPendingRecordEntity
                {
                    AppId = appId,
                    UserId = userId,
                    SessionId = sessionId,
                    StateJson = json,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                entity.StateJson = json;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.AgenticPendingRecords
                .FirstOrDefaultAsync(
                    x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entity is not null)
            {
                db.AgenticPendingRecords.Remove(entity);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
}
