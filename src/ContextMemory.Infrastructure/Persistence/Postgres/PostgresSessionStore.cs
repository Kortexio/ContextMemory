using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Persistence.Postgres;
using ContextMemory.Core.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ILogger<PostgresSessionStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public PostgresSessionStore(
        IDbContextFactory<ContextMemoryDbContext> dbFactory,
        ILogger<PostgresSessionStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<SessionSnapshot> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await LoadRecordAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
            return ToSnapshot(sessionId, record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task EnsureInitializedAsync(
        string appId,
        string userId,
        string sessionId,
        string appSchema,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var existing = await db.SessionRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
                return;

            var record = new SessionPersistenceRecord
            {
                IndexMd = SessionDefaults.EmptyIndex,
                LogMd = SessionDefaults.EmptyLog,
                SchemaMd = string.IsNullOrWhiteSpace(appSchema) ? SessionDefaults.DefaultSchema : appSchema,
                Messages = []
            };

            db.SessionRecords.Add(new SessionRecordEntity
            {
                AppId = appId,
                UserId = userId,
                SessionId = sessionId,
                DataJson = JsonSerializer.Serialize(record, JsonOptions),
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendMessagesAsync(
        string appId,
        string userId,
        string sessionId,
        IEnumerable<OllamaMessage> messages,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await LoadMutableRecordAsync(appId, userId, sessionId, cancellationToken)
                .ConfigureAwait(false);
            record.Messages.AddRange(messages);
            if (record.Messages.Count > maxMessages)
                record.Messages = record.Messages.Skip(record.Messages.Count - maxMessages).ToList();

            await SaveRecordAsync(appId, userId, sessionId, record, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplyWikiUpdateAsync(
        string appId,
        string userId,
        string sessionId,
        SessionWikiUpdate update,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await LoadMutableRecordAsync(appId, userId, sessionId, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(update.LogEntry))
                record.LogMd = record.LogMd.TrimEnd() + "\n\n" + update.LogEntry.Trim() + "\n";

            foreach (var page in update.Pages)
            {
                if (!SessionWikiPagePaths.TryNormalize(page.Path, out var relative))
                    continue;

                var name = Path.GetFileName(relative);
                record.Pages[name] = page.Content.Trim() + "\n";
                record.PageLastModified[name] = DateTimeOffset.UtcNow;
            }

            foreach (var deletePage in update.DeletePages)
            {
                if (!SessionWikiPagePaths.TryNormalize(deletePage, out var relative))
                    continue;

                var name = Path.GetFileName(relative);
                record.Pages.Remove(name);
                record.PageLastModified.Remove(name);
            }

            if (!string.IsNullOrWhiteSpace(update.IndexMd) || update.Pages.Count > 0)
                record.IndexMd = SessionWikiIndexBuilder.Reconcile(record.Pages.Keys, update.IndexMd);

            await SaveRecordAsync(appId, userId, sessionId, record, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DeleteSessionsOlderThanAsync(
        string appId,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var toDelete = await db.SessionRecords
            .Where(x => x.AppId == appId && x.UpdatedAt < olderThan)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (toDelete.Count == 0)
            return 0;

        var sessionIds = toDelete.Select(x => x.SessionId).ToList();
        var pending = await db.AgenticPendingRecords
            .Where(x => x.AppId == appId && sessionIds.Contains(x.SessionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.AgenticPendingRecords.RemoveRange(pending);
        db.SessionRecords.RemoveRange(toDelete);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var session in toDelete)
            _locks.TryRemove(session.SessionId, out _);

        _logger.LogInformation(
            "Deleted {Count} sessions older than {OlderThan} for app {AppId}",
            toDelete.Count,
            olderThan,
            appId);

        return toDelete.Count;
    }

    public async Task<int> DeleteSessionsForUserAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var toDelete = await db.SessionRecords
            .Where(x => x.AppId == appId && x.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (toDelete.Count == 0)
            return 0;

        var pending = await db.AgenticPendingRecords
            .Where(x => x.AppId == appId && x.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.AgenticPendingRecords.RemoveRange(pending);
        db.SessionRecords.RemoveRange(toDelete);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var session in toDelete)
            _locks.TryRemove(session.SessionId, out _);

        _logger.LogInformation(
            "Deleted {Count} sessions for app {AppId} user {UserId}",
            toDelete.Count,
            appId,
            userId);

        return toDelete.Count;
    }

    private async Task<SessionPersistenceRecord> LoadMutableRecordAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var record = await LoadRecordAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
        return record ?? new SessionPersistenceRecord
        {
            IndexMd = SessionDefaults.EmptyIndex,
            LogMd = SessionDefaults.EmptyLog,
            SchemaMd = SessionDefaults.DefaultSchema
        };
    }

    private async Task<SessionPersistenceRecord?> LoadRecordAsync(
        string appId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.SessionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return null;

        return JsonSerializer.Deserialize<SessionPersistenceRecord>(entity.DataJson, JsonOptions);
    }

    private async Task SaveRecordAsync(
        string appId,
        string userId,
        string sessionId,
        SessionPersistenceRecord record,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.SessionRecords
            .FirstOrDefaultAsync(
                x => x.SessionId == sessionId && x.AppId == appId && x.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

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

    private static SessionSnapshot ToSnapshot(string sessionId, SessionPersistenceRecord? record)
    {
        record ??= new SessionPersistenceRecord();
        return new SessionSnapshot
        {
            SessionPath = $"postgres://sessions/{sessionId}",
            IndexMd = record.IndexMd,
            LogMd = record.LogMd,
            SchemaMd = record.SchemaMd,
            Pages = record.Pages,
            PageLastModified = record.PageLastModified,
            Messages = record.Messages
        };
    }

    private SemaphoreSlim GetLock(string sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
}

internal sealed class SessionPersistenceRecord
{
    public string IndexMd { get; set; } = SessionDefaults.EmptyIndex;
    public string LogMd { get; set; } = SessionDefaults.EmptyLog;
    public string SchemaMd { get; set; } = SessionDefaults.DefaultSchema;
    public Dictionary<string, string> Pages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTimeOffset> PageLastModified { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OllamaMessage> Messages { get; set; } = [];
}

internal static class SessionWikiIndexBuilder
{
    public static string Reconcile(IEnumerable<string> pageNames, string? proposedIndexMd)
    {
        var pages = pageNames.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (pages.Count == 0)
            return string.IsNullOrWhiteSpace(proposedIndexMd)
                ? SessionDefaults.EmptyIndex.TrimEnd()
                : proposedIndexMd.Trim();

        if (!string.IsNullOrWhiteSpace(proposedIndexMd)
            && !SessionWikiHelpers.IsPlaceholderIndex(proposedIndexMd))
            return proposedIndexMd.Trim();

        var lines = new List<string> { "# Wiki index", string.Empty };
        foreach (var page in pages)
            lines.Add($"- [{page}](pages/{page}.md)");

        return string.Join('\n', lines);
    }
}
