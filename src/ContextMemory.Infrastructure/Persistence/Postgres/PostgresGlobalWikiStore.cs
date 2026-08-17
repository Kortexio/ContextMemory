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
            .FirstOrDefaultAsync(
                x => x.AppId == appId
                     && x.DocumentId == documentId
                     && x.Status == GlobalWikiRevisionStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToDocument(entity);
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (!includeSuperseded)
            query = query.Where(x => x.Status == GlobalWikiRevisionStatus.Active);
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
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (!includeSuperseded)
            query = query.Where(x => x.Status == GlobalWikiRevisionStatus.Active);
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
        GlobalWikiAliasLexicon.Invalidate(appId);
        var hash = GlobalWikiSlug.ComputeContentHash(request.Content);
        var existing = await db.GlobalWikiDocuments
            .FirstOrDefaultAsync(
                x => x.AppId == appId
                     && x.DocumentId == documentId
                     && x.Status == GlobalWikiRevisionStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        var slug = GlobalWikiSlug.FromDocumentId(documentId, request.Slug);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? GlobalWikiSlug.ExtractTitle(request.Content, documentId)
            : request.Title.Trim();
        var summary = GlobalWikiSlug.ExtractSummary(request.Content, request.Summary);
        var now = DateTimeOffset.UtcNow;
        var metadataJson = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>(), JsonOptions);
        var sourceId = request.SourceId?.Trim() ?? existing?.SourceId ?? string.Empty;

        if (existing is not null && string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
        {
            var metaOnlyChange =
                !string.Equals(existing.Summary, summary, StringComparison.Ordinal)
                || !string.Equals(existing.Title, title, StringComparison.Ordinal)
                || !string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal)
                || !string.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || (request.Metadata is not null && !string.Equals(existing.MetadataJson, metadataJson, StringComparison.Ordinal));

            if (!metaOnlyChange)
            {
                return new GlobalWikiUpsertResult
                {
                    AppId = appId,
                    DocumentId = documentId,
                    Slug = existing.Slug,
                    ContentHash = existing.ContentHash,
                    RevisionId = existing.RevisionId,
                    UpdatedAt = existing.UpdatedAt,
                    Created = false,
                    Unchanged = true
                };
            }

            existing.Slug = slug;
            existing.Title = title;
            existing.Summary = summary;
            existing.SourceId = sourceId;
            if (request.Metadata is not null)
                existing.MetadataJson = metadataJson;
            existing.UpdatedAt = now;
            if (request.ValidTo.HasValue)
                existing.ValidTo = request.ValidTo;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new GlobalWikiUpsertResult
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = slug,
                ContentHash = hash,
                RevisionId = existing.RevisionId,
                UpdatedAt = now,
                Created = false,
                Unchanged = false
            };
        }

        if (existing is not null && request.Overwrite)
        {
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
            if (request.ValidFrom.HasValue)
                existing.ValidFrom = request.ValidFrom.Value;
            if (request.ValidTo.HasValue)
                existing.ValidTo = request.ValidTo;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new GlobalWikiUpsertResult
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = slug,
                ContentHash = hash,
                RevisionId = existing.RevisionId,
                UpdatedAt = now,
                Created = false,
                Unchanged = false
            };
        }

        var validFrom = request.ValidFrom ?? now;
        var newRevisionId = Guid.NewGuid().ToString("N");
        var superseded = false;

        if (existing is not null)
        {
            existing.Status = GlobalWikiRevisionStatus.Superseded;
            existing.ValidTo = validFrom;
            existing.UpdatedAt = now;
            superseded = true;
        }

        var content = request.Content ?? string.Empty;
        db.GlobalWikiDocuments.Add(new GlobalWikiDocumentEntity
        {
            AppId = appId,
            DocumentId = documentId,
            RevisionId = newRevisionId,
            Slug = slug,
            Title = title,
            Content = content,
            Summary = summary,
            SourceId = sourceId,
            MetadataJson = metadataJson,
            ContentHash = hash,
            Status = GlobalWikiRevisionStatus.Active,
            ValidFrom = validFrom,
            ValidTo = request.ValidTo,
            SupersedesRevisionId = existing?.RevisionId,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new GlobalWikiUpsertResult
        {
            AppId = appId,
            DocumentId = documentId,
            Slug = slug,
            ContentHash = hash,
            RevisionId = newRevisionId,
            UpdatedAt = now,
            Created = existing is null,
            Unchanged = false,
            Superseded = superseded
        };
    }

    public async Task<bool> DeleteAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.GlobalWikiDocuments
            .FirstOrDefaultAsync(
                x => x.AppId == appId
                     && x.DocumentId == documentId
                     && x.Status == GlobalWikiRevisionStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        existing.Status = GlobalWikiRevisionStatus.Superseded;
        existing.ValidTo = now;
        existing.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GlobalWikiAliasLexicon.Invalidate(appId);
        return true;
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> GetAllForQueryAsync(
        string appId,
        string? sourceId = null,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var point = asOf ?? DateTimeOffset.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking()
            .Where(x => x.AppId == appId
                        && x.ValidFrom <= point
                        && (x.ValidTo == null || x.ValidTo > point));
        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId);

        var entities = await query
            .OrderByDescending(x => x.ValidFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities
            .GroupBy(x => x.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDocument(g.First()))
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> SearchAsync(
        string appId,
        string query,
        DateTimeOffset? asOf = null,
        string? sourceId = null,
        int topK = 50,
        bool digestOnly = false,
        CancellationToken cancellationToken = default)
    {
        topK = Math.Clamp(topK, 1, 200);
        var point = asOf ?? DateTimeOffset.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var lexicon = await LoadLexiconAsync(db, appId, point, cancellationToken).ConfigureAwait(false);
        GlobalWikiAliasLexicon.Remember(appId, lexicon);
        var expansion = lexicon.Expand(query);

        if (expansion.Groups.Count == 0)
            return [];

        var vectorColumn = digestOnly ? "digest_search_vector" : "search_vector";
        var strictQuery = GlobalWikiAliasLexicon.ToPostgresTsQuery(expansion);
        var orQuery = GlobalWikiAliasLexicon.ToPostgresOrTsQuery(expansion);

        List<GlobalWikiDocumentEntity> entities;
        try
        {
            entities = await QueryFtsAsync(
                    db,
                    appId,
                    point,
                    sourceId,
                    vectorColumn,
                    strictQuery,
                    topK,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entities.Count == 0 && !string.IsNullOrWhiteSpace(orQuery)
                && !string.Equals(orQuery, strictQuery, StringComparison.Ordinal))
            {
                entities = await QueryFtsAsync(
                        db,
                        appId,
                        point,
                        sourceId,
                        vectorColumn,
                        orQuery,
                        topK,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            return [];
        }

        if (entities.Count == 0)
            return [];

        return GlobalWikiScoring.ScoreMatches(
                entities.Select(ToDocument).ToList(),
                query,
                lexicon,
                digestOnly)
            .Take(topK)
            .Select(x => x.Document)
            .ToList();
    }

    private static async Task<List<GlobalWikiDocumentEntity>> QueryFtsAsync(
        ContextMemoryDbContext db,
        string appId,
        DateTimeOffset point,
        string? sourceId,
        string vectorColumn,
        string? tsQuery,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tsQuery))
            return [];

        var sql =
            "SELECT * FROM global_wiki_documents " +
            "WHERE \"AppId\" = {0} " +
            "AND \"ValidFrom\" <= {1} " +
            "AND (\"ValidTo\" IS NULL OR \"ValidTo\" > {1}) " +
            "AND ({2}::text IS NULL OR \"SourceId\" = {2}) " +
            $"AND {vectorColumn} @@ to_tsquery('simple', {{3}}) " +
            $"ORDER BY ts_rank({vectorColumn}, to_tsquery('simple', {{3}})) DESC " +
            "LIMIT {4}";

        return await db.GlobalWikiDocuments
            .FromSqlRaw(sql, appId, point, (object?)sourceId ?? DBNull.Value, tsQuery, topK)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<GlobalWikiAliasLexicon> LoadLexiconAsync(
        ContextMemoryDbContext db,
        string appId,
        DateTimeOffset point,
        CancellationToken cancellationToken)
    {
        var rows = await db.GlobalWikiDocuments.AsNoTracking()
            .Where(x => x.AppId == appId
                        && x.ValidFrom <= point
                        && (x.ValidTo == null || x.ValidTo > point))
            .Select(x => new { x.DocumentId, x.Title, x.Summary })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reserved = await db.GlobalWikiDocuments.AsNoTracking()
            .Where(x => x.AppId == appId
                        && x.ValidFrom <= point
                        && (x.ValidTo == null || x.ValidTo > point)
                        && (x.DocumentId == GlobalWikiCatalog.GlossaryDocumentId
                            || x.DocumentId == GlobalWikiCatalog.DocumentId))
            .Select(x => new { x.DocumentId, x.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? glossary = reserved
            .FirstOrDefault(r => GlobalWikiCatalog.IsGlossaryDocument(r.DocumentId))
            ?.Content;
        var harvest = rows
            .Select(r => (r.Title, r.Summary))
            .ToList();
        var catalog = reserved
            .FirstOrDefault(r => GlobalWikiCatalog.IsCatalogDocument(r.DocumentId))
            ?.Content;
        if (!string.IsNullOrWhiteSpace(catalog))
            harvest.Add((GlobalWikiCatalog.Title, catalog));

        return GlobalWikiAliasLexicon.FromHarvest(glossary, harvest);
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListRevisionsAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entities = await db.GlobalWikiDocuments.AsNoTracking()
            .Where(x => x.AppId == appId && x.DocumentId == documentId)
            .OrderByDescending(x => x.ValidFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(ToDocument).ToList();
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAuditAsync(
        string appId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.GlobalWikiDocuments.AsNoTracking().Where(x => x.AppId == appId);
        if (from.HasValue)
            query = query.Where(x => x.UpdatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.UpdatedAt <= to.Value);

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
            RevisionId = entity.RevisionId,
            ValidFrom = entity.ValidFrom == default ? entity.CreatedAt : entity.ValidFrom,
            ValidTo = entity.ValidTo,
            Status = string.IsNullOrWhiteSpace(entity.Status)
                ? GlobalWikiRevisionStatus.Active
                : entity.Status,
            SupersedesRevisionId = entity.SupersedesRevisionId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
