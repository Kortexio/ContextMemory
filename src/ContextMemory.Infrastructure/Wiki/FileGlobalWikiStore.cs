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
        var path = GetMetaPath(appId, documentId);
        if (!File.Exists(path))
            return null;

        var meta = await ReadMetaAsync(path, cancellationToken).ConfigureAwait(false);
        if (meta is null)
            return null;

        var contentPath = GetContentPath(appId, meta.Slug);
        var content = File.Exists(contentPath)
            ? await File.ReadAllTextAsync(contentPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        return ToDocument(appId, meta, content);
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> ListAsync(
        string appId,
        string? sourceId = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllForQueryAsync(appId, sourceId, cancellationToken).ConfigureAwait(false);
        return all.Skip(offset).Take(limit).ToList();
    }

    public async Task<int> CountAsync(
        string appId,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllForQueryAsync(appId, sourceId, cancellationToken).ConfigureAwait(false);
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
            Directory.CreateDirectory(GetAppDir(appId));
            Directory.CreateDirectory(GetPagesDir(appId));
            Directory.CreateDirectory(GetMetaDir(appId));

            var hash = GlobalWikiSlug.ComputeContentHash(request.Content);
            var existing = await GetAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
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
            var created = existing is null;

            // Remove old content file if slug changed
            if (existing is not null && !string.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                var oldContent = GetContentPath(appId, existing.Slug);
                if (File.Exists(oldContent))
                    File.Delete(oldContent);
            }

            var meta = new FileMeta
            {
                DocumentId = documentId,
                Slug = slug,
                Title = title,
                Summary = summary,
                SourceId = request.SourceId?.Trim() ?? existing?.SourceId ?? string.Empty,
                Metadata = request.Metadata ?? existing?.Metadata ?? new Dictionary<string, string>(),
                ContentHash = hash,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await File.WriteAllTextAsync(GetContentPath(appId, slug), request.Content ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    GetMetaPath(appId, documentId),
                    JsonSerializer.Serialize(meta, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            return new GlobalWikiUpsertResult
            {
                AppId = appId,
                DocumentId = documentId,
                Slug = slug,
                ContentHash = hash,
                UpdatedAt = now,
                Created = created,
                Unchanged = false
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
        var existing = await GetAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        var metaPath = GetMetaPath(appId, documentId);
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        var contentPath = GetContentPath(appId, existing.Slug);
        if (File.Exists(contentPath))
            File.Delete(contentPath);

        return true;
    }

    public async Task<IReadOnlyList<GlobalWikiDocument>> GetAllForQueryAsync(
        string appId,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var metaDir = GetMetaDir(appId);
        if (!Directory.Exists(metaDir))
            return [];

        var results = new List<GlobalWikiDocument>();
        foreach (var file in Directory.EnumerateFiles(metaDir, "*.json"))
        {
            var meta = await ReadMetaAsync(file, cancellationToken).ConfigureAwait(false);
            if (meta is null)
                continue;

            if (!string.IsNullOrWhiteSpace(sourceId)
                && !string.Equals(meta.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                continue;

            var contentPath = GetContentPath(appId, meta.Slug);
            var content = File.Exists(contentPath)
                ? await File.ReadAllTextAsync(contentPath, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            results.Add(ToDocument(appId, meta, content));
        }

        return results
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
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
            CreatedAt = meta.CreatedAt,
            UpdatedAt = meta.UpdatedAt
        };

    private static async Task<FileMeta?> ReadMetaAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<FileMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetAppDir(string appId) => Path.Combine(_root, appId);
    private string GetPagesDir(string appId) => Path.Combine(GetAppDir(appId), "pages");
    private string GetMetaDir(string appId) => Path.Combine(GetAppDir(appId), "meta");
    private string GetContentPath(string appId, string slug) => Path.Combine(GetPagesDir(appId), slug + ".md");

    private string GetMetaPath(string appId, string documentId)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(documentId))).ToLowerInvariant()[..32];
        return Path.Combine(GetMetaDir(appId), safe + ".json");
    }

    private sealed class FileMeta
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string ContentHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
