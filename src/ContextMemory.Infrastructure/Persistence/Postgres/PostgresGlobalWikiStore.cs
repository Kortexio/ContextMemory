using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresGlobalWikiStore : IGlobalWikiStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresGlobalWikiStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<GlobalWikiDocument?> GetAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.GlobalWikiDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppId == appId && x.DocumentId == documentId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToDocument(entity);
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId);

        var entities = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToDocument).ToList();
    }

    public async Task<int> CountAsync(
        string appId,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId);
        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = GlobalWikiSlug.ComputeContentHash(request.Content);
        var existing = await db.GlobalWikiDocuments
            .FirstOrDefaultAsync(x => x.AppId == appId && x.DocumentId == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null && string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
        {
            return new GlobalWikiUpsertResult
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = existing.Slug,
                ContentHash = existing.ContentHash,
                UpdatedAt = existing.UpdatedAt,
                Created = false,
                Unchanged = true
            };
        }

        var slug = GlobalWikiSlug.FromDocumentId(documentId, request.Slug);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? GlobalWikiSlug.ExtractTitle(request.Content, documentId)
            : request.Title.Trim();
        var summary = GlobalWikiSlug.ExtractSummary(request.Content, request.Summary);
        var now = DateTimeOffset.UtcNow;
        var metadataJson = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>(), JsonOptions);

        if (existing is null)
        {
            db.GlobalWikiDocuments.Add(new GlobalWikiDocumentEntity
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = slug,
                Title = title,
                Content = request.Content ?? string.Empty,
                Summary = summary,
                SourceId = request.SourceId?.Trim() ?? string.Empty,
                MetadataJson = metadataJson,
                ContentHash = hash,
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new GlobalWikiUpsertResult
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = slug,
                ContentHash = hash,
                UpdatedAt = now,
                Created = true,
                Unchanged = false
            };
        }

        existing.Slug = slug;
        existing.Title = title;
        existing.Content = request.Content ?? string.Empty;
        existing.Summary = summary;
        if (request.SourceId is not null)
            existing.SourceId = request.SourceId.Trim();
        if (request.Metadata is not null)
            existing.MetadataJson = metadataJson;
        existing.ContentHash = hash;
        existing.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new GlobalWikiUpsertResult
        {
            AppId = appId,
            DocumentId = documentId,
            Slug = slug,
            ContentHash = hash,
            UpdatedAt = now,
            Created = false,
            Unchanged = false
        };
    }

    public async Task<bool> DeleteAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.GlobalWikiDocuments
            .FirstOrDefaultAsync(x => x.AppId == appId && x.DocumentId == documentId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
            return false;

        db.GlobalWikiDocuments.Remove(existing);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> GetAllForQueryAsync(
        string appId,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId);

        var entities = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToDocument).ToList();
    }

    private static GlobalWikiDocument ToDocument(GlobalWikiDocumentEntity entity)
    {
        Dictionary<string, string> metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MetadataJson, JsonOptions)
                       ?? new Dictionary<string, string>();
        }
        catch
        {
            metadata = new Dictionary<string, string>();
        }

        return new GlobalWikiDocument
        {
            AppId = entity.AppId,
            DocumentId = entity.DocumentId,
            Slug = entity.Slug,
            Title = entity.Title,
            Content = entity.Content,
            Summary = entity.Summary,
            SourceId = entity.SourceId,
            Metadata = metadata,
            ContentHash = entity.ContentHash,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
