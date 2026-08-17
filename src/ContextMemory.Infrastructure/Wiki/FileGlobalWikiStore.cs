using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Wiki;

public sealed class FileGlobalWikiStore : IGlobalWikiStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileGlobalWikiStore(IOptions<ContextMemoryOptions> options)
    {
        var cfg = options.Value;
        _root = Path.Combine(Path.GetFullPath(cfg.DataPath, cfg.ContentRootPath), "global-wiki");
        Directory.CreateDirectory(_root);
    }

    public async Task<GlobalWikiDocument?> GetAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var meta = await ReadActiveMetaAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        if (meta is null || !string.Equals(meta.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal))
            return null;

        var content = await ReadContentAsync(appId, meta, cancellationToken).ConfigureAwait(false);
        return ToDocument(appId, meta, content);
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default)
    {
        var all = includeSuperseded
            ? await LoadAllRevisionsAsync(appId, sourceId, cancellationToken).ConfigureAwait(false)
            : await GetAllForQueryAsync(appId, sourceId, asOf: null, cancellationToken).ConfigureAwait(false);
        return all.Skip(offset).Take(limit).ToList();
    }

    public async Task<int> CountAsync(
        string appId,
        string? sourceId = null,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default)
    {
        var all = includeSuperseded
            ? await LoadAllRevisionsAsync(appId, sourceId, cancellationToken).ConfigureAwait(false)
            : await GetAllForQueryAsync(appId, sourceId, asOf: null, cancellationToken).ConfigureAwait(false);
        return all.Count;
    }

    public async Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd($"{appId}:{documentId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GlobalWikiAliasLexicon.Invalidate(appId);
            EnsureDirs(appId, documentId);

            var hash = GlobalWikiSlug.ComputeContentHash(request.Content);
            var existing = await ReadActiveMetaAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
            var slug = GlobalWikiSlug.FromDocumentId(documentId, request.Slug);
            var title = string.IsNullOrWhiteSpace(request.Title)
                ? GlobalWikiSlug.ExtractTitle(request.Content, documentId)
                : request.Title.Trim();
            var summary = GlobalWikiSlug.ExtractSummary(request.Content, request.Summary);
            var sourceId = request.SourceId?.Trim() ?? existing?.SourceId ?? string.Empty;
            var now = DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? existing?.Metadata ?? new Dictionary<string, string>();

            if (existing is not null
                && string.Equals(existing.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal)
                && string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
            {
                var metaOnlyChange =
                    !string.Equals(existing.Summary, summary, StringComparison.Ordinal)
                    || !string.Equals(existing.Title, title, StringComparison.Ordinal)
                    || !string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal)
                    || !string.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase)
                    || (request.Metadata is not null && !MetadataEquals(existing.Metadata, request.Metadata));

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

                if (!string.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase))
                {
                    var oldContent = GetRevisionContentPath(appId, existing.Slug, existing.RevisionId);
                    var newContent = GetRevisionContentPath(appId, slug, existing.RevisionId);
                    if (File.Exists(oldContent))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newContent)!);
                        File.Move(oldContent, newContent, overwrite: true);
                    }
                }

                existing.Slug = slug;
                existing.Title = title;
                existing.Summary = summary;
                existing.SourceId = sourceId;
                existing.Metadata = metadata;
                existing.UpdatedAt = now;
                if (request.ValidTo.HasValue)
                    existing.ValidTo = request.ValidTo;
                await WriteMetaAsync(appId, existing, cancellationToken).ConfigureAwait(false);
                await RebuildIndexAsync(appId, cancellationToken).ConfigureAwait(false);

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

            // Legacy overwrite: mutate active revision in place.
            if (existing is not null
                && request.Overwrite
                && string.Equals(existing.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal))
            {
                if (!string.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase))
                {
                    var oldPath = GetRevisionContentPath(appId, existing.Slug, existing.RevisionId);
                    if (File.Exists(oldPath))
                        File.Delete(oldPath);
                }

                existing.Slug = slug;
                existing.Title = title;
                existing.Summary = summary;
                existing.SourceId = sourceId;
                existing.Metadata = metadata;
                existing.ContentHash = hash;
                existing.UpdatedAt = now;
                if (request.ValidFrom.HasValue)
                    existing.ValidFrom = request.ValidFrom.Value;
                if (request.ValidTo.HasValue)
                    existing.ValidTo = request.ValidTo;

                await File.WriteAllTextAsync(
                        GetRevisionContentPath(appId, slug, existing.RevisionId),
                        request.Content ?? string.Empty,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteMetaAsync(appId, existing, cancellationToken).ConfigureAwait(false);
                await RebuildIndexAsync(appId, cancellationToken).ConfigureAwait(false);

                return new GlobalWikiUpsertResult
                {
                    AppId = appId,
                    DocumentId = documentId,
                    Slug = slug,
                    ContentHash = hash,
                    RevisionId = existing.RevisionId,
                    UpdatedAt = now,
                    Created = false,
                    Unchanged = false,
                    Superseded = false
                };
            }

            var newRevisionId = Guid.NewGuid().ToString("N");
            var validFrom = request.ValidFrom ?? now;
            var superseded = false;

            if (existing is not null
                && string.Equals(existing.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal))
            {
                existing.Status = GlobalWikiRevisionStatus.Superseded;
                existing.ValidTo = validFrom;
                existing.UpdatedAt = now;
                await WriteMetaAsync(appId, existing, cancellationToken).ConfigureAwait(false);
                superseded = true;
            }

            var meta = new FileMeta
            {
                DocumentId = documentId,
                RevisionId = newRevisionId,
                Slug = slug,
                Title = title,
                Summary = summary,
                SourceId = sourceId,
                Metadata = metadata,
                ContentHash = hash,
                Status = GlobalWikiRevisionStatus.Active,
                ValidFrom = validFrom,
                ValidTo = request.ValidTo,
                SupersedesRevisionId = existing?.RevisionId,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await File.WriteAllTextAsync(
                    GetRevisionContentPath(appId, slug, newRevisionId),
                    request.Content ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteMetaAsync(appId, meta, cancellationToken).ConfigureAwait(false);
            await WriteActivePointerAsync(appId, documentId, newRevisionId, cancellationToken).ConfigureAwait(false);
            await RebuildIndexAsync(appId, cancellationToken).ConfigureAwait(false);

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
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var existing = await ReadActiveMetaAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        if (existing is null
            || !string.Equals(existing.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal))
            return false;

        var now = DateTimeOffset.UtcNow;
        existing.Status = GlobalWikiRevisionStatus.Superseded;
        existing.ValidTo = now;
        existing.UpdatedAt = now;
        await WriteMetaAsync(appId, existing, cancellationToken).ConfigureAwait(false);
        var pointer = GetActivePointerPath(appId, documentId);
        if (File.Exists(pointer))
            File.Delete(pointer);
        await RebuildIndexAsync(appId, cancellationToken).ConfigureAwait(false);
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
        var all = await LoadAllRevisionsAsync(appId, sourceId, cancellationToken).ConfigureAwait(false);
        return all
            .Where(d => d.IsValidAt(point))
            .GroupBy(d => d.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.ValidFrom).First())
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
        var docs = await GetAllForQueryAsync(appId, sourceId, asOf, cancellationToken).ConfigureAwait(false);
        var index = await ReadIndexAsync(appId, digestOnly, cancellationToken).ConfigureAwait(false);
        var lexicon = GlobalWikiAliasLexicon.FromDocuments(docs);
        GlobalWikiAliasLexicon.Remember(appId, lexicon);
        var expansion = lexicon.Expand(query);
        var tokens = expansion.IndexTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (tokens.Count == 0)
            return [];

        HashSet<string>? candidateIds = null;
        if (index is { Count: > 0 })
        {
            candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in tokens)
            {
                if (index.TryGetValue(token, out var ids))
                {
                    foreach (var id in ids)
                        candidateIds.Add(id);
                }
            }
        }

        var pool = candidateIds is { Count: > 0 }
            ? docs.Where(d => candidateIds.Contains(d.DocumentId)).ToList()
            : [];

        if (pool.Count == 0)
            return [];

        return GlobalWikiScoring.ScoreMatches(pool, query, lexicon, digestOnly)
            .Take(Math.Clamp(topK, 1, 200))
            .Select(x => x.Document)
            .ToList();
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListRevisionsAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var all = await LoadAllRevisionsAsync(appId, sourceId: null, cancellationToken).ConfigureAwait(false);
        return all
            .Where(d => string.Equals(d.DocumentId, documentId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.ValidFrom)
            .ToList();
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAuditAsync(
        string appId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var all = await LoadAllRevisionsAsync(appId, sourceId: null, cancellationToken).ConfigureAwait(false);
        return all
            .Where(d =>
            {
                if (from.HasValue && d.UpdatedAt < from.Value)
                    return false;
                if (to.HasValue && d.UpdatedAt > to.Value)
                    return false;
                return true;
            })
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<GlobalWikiDocument>> LoadAllRevisionsAsync(
        string appId,
        string? sourceId,
        CancellationToken cancellationToken)
    {
        var revDir = GetRevisionsDir(appId);
        if (!Directory.Exists(revDir))
        {
            // Migrate legacy single-meta layout on first read.
            await MigrateLegacyAsync(appId, cancellationToken).ConfigureAwait(false);
        }

        if (!Directory.Exists(revDir))
            return [];

        var results = new List<GlobalWikiDocument>();
        foreach (var file in Directory.EnumerateFiles(revDir, "*.json", SearchOption.AllDirectories))
        {
            var meta = await ReadMetaFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (meta is null)
                continue;
            if (!string.IsNullOrWhiteSpace(sourceId)
                && !string.Equals(meta.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                continue;

            var content = await ReadContentAsync(appId, meta, cancellationToken).ConfigureAwait(false);
            results.Add(ToDocument(appId, meta, content));
        }

        return results;
    }

    private async Task MigrateLegacyAsync(string appId, CancellationToken cancellationToken)
    {
        var revDir = GetRevisionsDir(appId);
        if (Directory.Exists(revDir) && Directory.EnumerateFileSystemEntries(revDir).Any())
            return;

        var legacyMetaDir = GetMetaDir(appId);
        if (!Directory.Exists(legacyMetaDir))
            return;

        foreach (var file in Directory.EnumerateFiles(legacyMetaDir, "*.json"))
        {
            if (Path.GetFileName(file).EndsWith(".active", StringComparison.OrdinalIgnoreCase))
                continue;

            var meta = await ReadMetaFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (meta is null || string.IsNullOrWhiteSpace(meta.DocumentId))
                continue;

            if (string.IsNullOrWhiteSpace(meta.RevisionId))
                meta.RevisionId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(meta.Status))
                meta.Status = GlobalWikiRevisionStatus.Active;
            if (meta.ValidFrom == default)
                meta.ValidFrom = meta.CreatedAt == default ? DateTimeOffset.UtcNow : meta.CreatedAt;

            EnsureDirs(appId, meta.DocumentId);

            var legacyContent = Path.Combine(GetPagesDir(appId), meta.Slug + ".md");
            var newContent = GetRevisionContentPath(appId, meta.Slug, meta.RevisionId);
            if (File.Exists(legacyContent) && !File.Exists(newContent))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newContent)!);
                File.Copy(legacyContent, newContent);
            }

            await WriteMetaAsync(appId, meta, cancellationToken).ConfigureAwait(false);
            if (string.Equals(meta.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal))
                await WriteActivePointerAsync(appId, meta.DocumentId, meta.RevisionId, cancellationToken).ConfigureAwait(false);
        }

        await RebuildIndexAsync(appId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RebuildIndexAsync(string appId, CancellationToken cancellationToken)
    {
        var docs = await GetAllForQueryAsync(appId, sourceId: null, asOf: null, cancellationToken).ConfigureAwait(false);
        var fullIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var digestIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            IndexDocumentTokens(fullIndex, doc.DocumentId, $"{doc.DocumentId} {doc.Slug} {doc.Title} {doc.Summary} {doc.SourceId} {doc.Content}");
            IndexDocumentTokens(digestIndex, doc.DocumentId, GlobalWikiDigestFields.DigestIndexText(doc));
        }

        Directory.CreateDirectory(GetAppDir(appId));
        await WriteIndexFileAsync(GetIndexPath(appId), fullIndex, cancellationToken).ConfigureAwait(false);
        await WriteIndexFileAsync(GetDigestIndexPath(appId), digestIndex, cancellationToken).ConfigureAwait(false);
    }

    private static void IndexDocumentTokens(
        Dictionary<string, HashSet<string>> index,
        string documentId,
        string text)
    {
        foreach (var token in GlobalWikiScoring.Tokenize(text))
        {
            if (!index.TryGetValue(token, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                index[token] = set;
            }

            set.Add(documentId);
        }
    }

    private static async Task WriteIndexFileAsync(
        string path,
        Dictionary<string, HashSet<string>> index,
        CancellationToken cancellationToken)
    {
        var serializable = index.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(serializable, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, List<string>>?> ReadIndexAsync(
        string appId,
        bool digestOnly,
        CancellationToken cancellationToken)
    {
        var path = digestOnly ? GetDigestIndexPath(appId) : GetIndexPath(appId);
        if (!File.Exists(path))
        {
            if (digestOnly)
                return await ReadIndexAsync(appId, digestOnly: false, cancellationToken).ConfigureAwait(false);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetDigestIndexPath(string appId) =>
        Path.Combine(GetAppDir(appId), "digest-index.json");

    private static bool MetadataEquals(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static GlobalWikiDocument ToDocument(string appId, FileMeta meta, string content) =>
        new()
        {
            AppId = appId,
            DocumentId = meta.DocumentId,
            Slug = meta.Slug,
            Title = meta.Title,
            Content = content,
            Summary = meta.Summary,
            SourceId = meta.SourceId,
            Metadata = meta.Metadata,
            ContentHash = meta.ContentHash,
            RevisionId = meta.RevisionId,
            ValidFrom = meta.ValidFrom == default ? meta.CreatedAt : meta.ValidFrom,
            ValidTo = meta.ValidTo,
            Status = string.IsNullOrWhiteSpace(meta.Status) ? GlobalWikiRevisionStatus.Active : meta.Status,
            SupersedesRevisionId = meta.SupersedesRevisionId,
            CreatedAt = meta.CreatedAt,
            UpdatedAt = meta.UpdatedAt
        };

    private async Task<FileMeta?> ReadActiveMetaAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken)
    {
        await MigrateLegacyAsync(appId, cancellationToken).ConfigureAwait(false);
        var pointer = GetActivePointerPath(appId, documentId);
        if (File.Exists(pointer))
        {
            var revisionId = (await File.ReadAllTextAsync(pointer, cancellationToken).ConfigureAwait(false)).Trim();
            var path = GetRevisionMetaPath(appId, documentId, revisionId);
            return await ReadMetaFileAsync(path, cancellationToken).ConfigureAwait(false);
        }

        // Fallback: any active revision for document
        var revs = await ListRevisionsAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        var active = revs.FirstOrDefault(r =>
            string.Equals(r.Status, GlobalWikiRevisionStatus.Active, StringComparison.Ordinal));
        if (active is null)
            return null;

        return new FileMeta
        {
            DocumentId = active.DocumentId,
            RevisionId = active.RevisionId,
            Slug = active.Slug,
            Title = active.Title,
            Summary = active.Summary,
            SourceId = active.SourceId,
            Metadata = active.Metadata,
            ContentHash = active.ContentHash,
            Status = active.Status,
            ValidFrom = active.ValidFrom,
            ValidTo = active.ValidTo,
            SupersedesRevisionId = active.SupersedesRevisionId,
            CreatedAt = active.CreatedAt,
            UpdatedAt = active.UpdatedAt
        };
    }

    private async Task WriteMetaAsync(string appId, FileMeta meta, CancellationToken cancellationToken)
    {
        var path = GetRevisionMetaPath(appId, meta.DocumentId, meta.RevisionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(meta, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteActivePointerAsync(
        string appId,
        string documentId,
        string revisionId,
        CancellationToken cancellationToken)
    {
        var path = GetActivePointerPath(appId, documentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, revisionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadContentAsync(string appId, FileMeta meta, CancellationToken cancellationToken)
    {
        var path = GetRevisionContentPath(appId, meta.Slug, meta.RevisionId);
        if (File.Exists(path))
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        var legacy = Path.Combine(GetPagesDir(appId), meta.Slug + ".md");
        if (File.Exists(legacy))
            return await File.ReadAllTextAsync(legacy, cancellationToken).ConfigureAwait(false);

        return string.Empty;
    }

    private static async Task<FileMeta?> ReadMetaFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<FileMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void EnsureDirs(string appId, string documentId)
    {
        Directory.CreateDirectory(GetAppDir(appId));
        Directory.CreateDirectory(GetPagesDir(appId));
        Directory.CreateDirectory(GetMetaDir(appId));
        Directory.CreateDirectory(GetDocumentRevisionDir(appId, documentId));
    }

    private string GetAppDir(string appId) => Path.Combine(_root, appId);
    private string GetPagesDir(string appId) => Path.Combine(GetAppDir(appId), "pages");
    private string GetMetaDir(string appId) => Path.Combine(GetAppDir(appId), "meta");
    private string GetRevisionsDir(string appId) => Path.Combine(GetAppDir(appId), "revisions");
    private string GetIndexPath(string appId) => Path.Combine(GetAppDir(appId), "search-index.json");

    private string SafeDocumentKey(string documentId) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(documentId))).ToLowerInvariant()[..32];

    private string GetDocumentRevisionDir(string appId, string documentId) =>
        Path.Combine(GetRevisionsDir(appId), SafeDocumentKey(documentId));

    private string GetRevisionMetaPath(string appId, string documentId, string revisionId) =>
        Path.Combine(GetDocumentRevisionDir(appId, documentId), revisionId + ".json");

    private string GetActivePointerPath(string appId, string documentId) =>
        Path.Combine(GetMetaDir(appId), SafeDocumentKey(documentId) + ".active");

    private string GetRevisionContentPath(string appId, string slug, string revisionId) =>
        Path.Combine(GetPagesDir(appId), $"{slug}@{revisionId}.md");

    private sealed class FileMeta
    {
        public string DocumentId { get; set; } = string.Empty;
        public string RevisionId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string ContentHash { get; set; } = string.Empty;
        public string Status { get; set; } = GlobalWikiRevisionStatus.Active;
        public DateTimeOffset ValidFrom { get; set; }
        public DateTimeOffset? ValidTo { get; set; }
        public string? SupersedesRevisionId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
