using System.Text;
using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.GlobalWiki;

public sealed class GlobalWikiService
{
    public const int DefaultTopK = 5;
    public const int DefaultBudgetChars = 8_000;
    public const int DefaultDigestTopK = 3;
    public const int DefaultDigestBudgetChars = 2_500;

    private readonly IGlobalWikiStore _store;
    private readonly IGlobalWikiDigestGenerator _digestGenerator;
    private readonly ILogger<GlobalWikiService> _logger;

    public GlobalWikiService(
        IGlobalWikiStore store,
        IGlobalWikiDigestGenerator digestGenerator,
        ILogger<GlobalWikiService> logger)
    {
        _store = store;
        _digestGenerator = digestGenerator;
        _logger = logger;
    }

    public async Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        // Ingest is storage-only. LLM digests run afterwards via RebuildDigestsAsync.
        return await _store.UpsertAsync(appId, documentId, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GlobalWikiBatchUpsertResult> UpsertBatchAsync(
        string appId,
        GlobalWikiBatchUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GlobalWikiUpsertResult>();
        foreach (var doc in request.Documents)
        {
            if (string.IsNullOrWhiteSpace(doc.DocumentId) || string.IsNullOrWhiteSpace(doc.Content))
                continue;
            if (GlobalWikiCatalog.IsReservedDocument(doc.DocumentId))
                continue;

            var result = await _store.UpsertAsync(
                appId,
                doc.DocumentId,
                new GlobalWikiUpsertRequest
                {
                    Title = doc.Title,
                    Content = doc.Content,
                    Summary = doc.Summary,
                    SourceId = doc.SourceId,
                    Metadata = doc.Metadata,
                    Slug = doc.Slug,
                    Overwrite = doc.Overwrite,
                    ValidFrom = doc.ValidFrom,
                    ValidTo = doc.ValidTo
                },
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new GlobalWikiBatchUpsertResult { Results = results };
    }

    /// <summary>
    /// After ingest completes, generate LLM digests (keywords + ≤6 lines) and refresh <c>wiki:catalog</c>.
    /// </summary>
    public async Task<GlobalWikiDigestRebuildResult> RebuildDigestsAsync(
        string appId,
        GlobalWikiDigestRebuildRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new GlobalWikiDigestRebuildRequest();
        var docs = await _store
            .GetAllForQueryAsync(appId, request.SourceId, asOf: null, cancellationToken)
            .ConfigureAwait(false);

        var candidates = docs
            .Where(d => !GlobalWikiCatalog.IsReservedDocument(d.DocumentId))
            .OrderBy(d => d.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updated = 0;
        var skipped = 0;

        foreach (var doc in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!request.Force && HasLlmDigest(doc.Summary))
            {
                skipped++;
                continue;
            }

            var digest = await _digestGenerator
                .GenerateAsync(
                    appId,
                    doc.DocumentId,
                    doc.Title,
                    doc.SourceId,
                    doc.Content,
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(digest))
            {
                digest = GlobalWikiDigestGenerator.BuildFallbackDigest(doc.DocumentId, doc.Title, doc.Content);
            }

            if (string.Equals(doc.Summary?.Trim(), digest.Trim(), StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            // Same content hash → meta-only update on active revision (no supersede).
            await _store.UpsertAsync(
                appId,
                doc.DocumentId,
                new GlobalWikiUpsertRequest
                {
                    Title = doc.Title,
                    Content = doc.Content,
                    Summary = digest,
                    SourceId = doc.SourceId,
                    Metadata = doc.Metadata,
                    Slug = doc.Slug
                },
                cancellationToken).ConfigureAwait(false);
            updated++;
        }

        await RefreshCatalogAsync(appId, cancellationToken).ConfigureAwait(false);
        var glossary = await RefreshGlossaryAsync(appId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Wiki digests rebuilt for {AppId}: processed={Processed}, updated={Updated}, skipped={Skipped}, glossaryPairs={GlossaryPairs}",
            appId,
            candidates.Count,
            updated,
            skipped,
            glossary.PairCount);

        return new GlobalWikiDigestRebuildResult
        {
            AppId = appId,
            Processed = candidates.Count,
            Updated = updated,
            Skipped = skipped,
            CatalogRefreshed = true,
            GlossaryRefreshed = glossary.Refreshed,
            GlossaryPairs = glossary.PairCount
        };
    }

    public async Task<bool> DeleteAsync(string appId, string documentId, CancellationToken cancellationToken = default)
    {
        if (GlobalWikiCatalog.IsReservedDocument(documentId))
            return await _store.DeleteAsync(appId, documentId, cancellationToken).ConfigureAwait(false);

        var deleted = await _store.DeleteAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            await RefreshCatalogAsync(appId, cancellationToken).ConfigureAwait(false);
            await RefreshGlossaryAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        return deleted;
    }

    private static bool HasLlmDigest(string? summary) =>
        !string.IsNullOrWhiteSpace(summary)
        && summary.TrimStart().StartsWith("Keywords:", StringComparison.OrdinalIgnoreCase);

    public async Task<GlobalWikiListResult> ListAsync(
        string appId,
        string? sourceId,
        int offset,
        int limit,
        bool includeSuperseded = false,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

        var total = await _store.CountAsync(appId, sourceId, includeSuperseded, cancellationToken)
            .ConfigureAwait(false);
        var docs = await _store.ListAsync(appId, sourceId, offset, limit, includeSuperseded, cancellationToken)
            .ConfigureAwait(false);

        return new GlobalWikiListResult
        {
            Offset = offset,
            Limit = limit,
            Total = total,
            Documents = docs.Select(ToSummary).ToList()
        };
    }

    public async Task<GlobalWikiQueryResult> QueryAsync(
        string appId,
        GlobalWikiQueryRequest request,
        int? defaultBudgetChars = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var topK = request.TopK > 0
            ? Math.Min(request.TopK, 50)
            : request.DigestOnly ? DefaultDigestTopK : DefaultTopK;
        var budget = request.BudgetChars > 0
            ? request.BudgetChars
            : defaultBudgetChars is > 0
                ? defaultBudgetChars.Value
                : request.DigestOnly ? DefaultDigestBudgetChars : DefaultBudgetChars;

        var matchedDocs = (await _store
                .SearchAsync(
                    appId,
                    request.Query,
                    asOf,
                    request.SourceId,
                    topK,
                    request.DigestOnly,
                    cancellationToken)
                .ConfigureAwait(false))
            .Where(d => !GlobalWikiCatalog.IsGlossaryDocument(d.DocumentId))
            .ToList();

        var totalDocs = await _store
            .CountAsync(appId, request.SourceId, includeSuperseded: false, cancellationToken)
            .ConfigureAwait(false);

        var matches = matchedDocs
            .Select((d, i) => new GlobalWikiMatch
            {
                DocumentId = d.DocumentId,
                Slug = d.Slug,
                Title = d.Title,
                Score = matchedDocs.Count - i,
                SourceId = d.SourceId,
                RevisionId = d.RevisionId
            })
            .ToList();

        // Prefer scored ordering from GlobalWikiScoring when we have the pool.
        if (matchedDocs.Count > 0)
        {
            var lexicon = GlobalWikiAliasLexicon.ForApp(appId);
            var rescored = GlobalWikiScoring.ScoreMatches(
                matchedDocs,
                request.Query,
                lexicon,
                request.DigestOnly).ToList();
            matchedDocs = rescored.Select(x => x.Document).ToList();
            matches = rescored
                .Select(m => new GlobalWikiMatch
                {
                    DocumentId = m.Document.DocumentId,
                    Slug = m.Document.Slug,
                    Title = m.Document.Title,
                    Score = m.Score,
                    SourceId = m.Document.SourceId,
                    RevisionId = m.Document.RevisionId
                })
                .ToList();
        }

        if (matchedDocs.Count == 0)
        {
            return new GlobalWikiQueryResult
            {
                CompiledMarkdown = string.Empty,
                CharCount = 0,
                IncludedDocuments = 0,
                TotalDocuments = totalDocs,
                Truncated = false,
                AsOf = asOf,
                Matches = matches
            };
        }

        var catalogIsPrimary = matchedDocs.Count > 0
            && GlobalWikiCatalog.IsCatalogDocument(matchedDocs[0].DocumentId);
        var pages = matchedDocs.ToDictionary(
            d => d.Slug,
            d => ResolvePackContent(d, catalogIsPrimary, request.DigestOnly),
            StringComparer.OrdinalIgnoreCase);
        var lastModified = matchedDocs.ToDictionary(d => d.Slug, d => d.UpdatedAt, StringComparer.OrdinalIgnoreCase);

        var snapshot = new SessionSnapshot
        {
            SessionPath = $"global://{appId}",
            IndexMd = string.Empty,
            LogMd = string.Empty,
            SchemaMd = string.Empty,
            Pages = pages,
            PageLastModified = lastModified,
            Messages = []
        };

        var compiled = SessionWikiCompiler.Compile(
            snapshot,
            request.Query,
            budget,
            includeIndex: false);

        var markdown = compiled.Content;
        var truncated = compiled.Truncated;
        var charCount = compiled.CharCount;

        if (request.IncludeIndex)
        {
            var remaining = budget - charCount;
            if (remaining > 120)
            {
                const string indexTruncatedNote = "\n\n_(… index truncated)_";
                var indexBlock = "\n\n## Index\n" + BuildIndex(matchedDocs);
                if (indexBlock.Length > remaining)
                {
                    var keep = Math.Max(0, remaining - indexTruncatedNote.Length);
                    indexBlock = indexBlock[..keep] + indexTruncatedNote;
                    truncated = true;
                }

                markdown += indexBlock;
                charCount = markdown.Length;
            }
            else if (matchedDocs.Count > compiled.IncludedPages)
            {
                truncated = true;
            }
        }

        return new GlobalWikiQueryResult
        {
            CompiledMarkdown = markdown,
            CharCount = charCount,
            IncludedDocuments = compiled.IncludedPages,
            TotalDocuments = totalDocs,
            Truncated = truncated,
            AsOf = asOf,
            Matches = matches
        };
    }

    public async Task<GlobalWikiGrepResult> GrepAsync(
        string appId,
        GlobalWikiGrepRequest request,
        int? defaultBudgetChars = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var maxHits = request.MaxHits > 0 ? Math.Min(request.MaxHits, 200) : 40;
        var budget = request.BudgetChars > 0
            ? request.BudgetChars
            : defaultBudgetChars is > 0 ? defaultBudgetChars.Value : DefaultBudgetChars;

        Regex regex;
        try
        {
            regex = new Regex(
                request.Pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline,
                TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            return new GlobalWikiGrepResult
            {
                CompiledMarkdown = $"Invalid regex: {ex.Message}",
                HitCount = 0,
                Truncated = false,
                AsOf = asOf
            };
        }

        var docs = await _store
            .GetAllForQueryAsync(appId, request.SourceId, asOf, cancellationToken)
            .ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"# wiki_grep `{request.Pattern}` (asOf={asOf:O})");
        sb.AppendLine();
        var hits = 0;
        var truncated = false;

        foreach (var doc in docs)
        {
            if (hits >= maxHits || sb.Length >= budget)
            {
                truncated = true;
                break;
            }

            var haystack = (doc.Content ?? string.Empty) + "\n" + (doc.Summary ?? string.Empty);
            if (string.IsNullOrWhiteSpace(haystack))
                continue;

            MatchCollection matches;
            try
            {
                matches = regex.Matches(haystack);
            }
            catch (RegexMatchTimeoutException)
            {
                truncated = true;
                break;
            }

            if (matches.Count == 0)
                continue;

            foreach (Match m in matches)
            {
                if (hits >= maxHits || sb.Length >= budget)
                {
                    truncated = true;
                    break;
                }

                var line = ExtractLineContext(haystack, m.Index, m.Length);
                sb.AppendLine($"- `{doc.DocumentId}`:{ApproxLineNumber(haystack, m.Index)}: {line}");
                hits++;
            }
        }

        if (hits == 0)
            sb.AppendLine("_No matches._");

        return new GlobalWikiGrepResult
        {
            CompiledMarkdown = sb.ToString().TrimEnd(),
            HitCount = hits,
            Truncated = truncated,
            AsOf = asOf
        };
    }

    public async Task<GlobalWikiRevisionListResult> ListRevisionsAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var revs = await _store.ListRevisionsAsync(appId, documentId, cancellationToken).ConfigureAwait(false);
        return new GlobalWikiRevisionListResult
        {
            DocumentId = documentId,
            Revisions = revs.Select(ToSummary).ToList()
        };
    }

    private static string ExtractLineContext(string text, int index, int length)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = text.IndexOf('\n', index + length);
        if (end < 0)
            end = text.Length;
        var line = text[start..end].Trim();
        if (line.Length > 240)
            line = line[..240] + "…";
        return line;
    }

    private static int ApproxLineNumber(string text, int index)
    {
        var n = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                n++;
        }

        return n;
    }

    public async Task<GlobalWikiAuditExportResult> ExportAuditAsync(
        string appId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var revs = await _store.ListAuditAsync(appId, from, to, cancellationToken).ConfigureAwait(false);
        return new GlobalWikiAuditExportResult
        {
            AppId = appId,
            From = from,
            To = to,
            Revisions = revs.Select(ToSummary).ToList()
        };
    }

    public Task<GlobalWikiDocument?> GetAsync(
        string appId,
        string documentId,
        CancellationToken cancellationToken = default) =>
        _store.GetAsync(appId, documentId, cancellationToken);

    private async Task RefreshCatalogAsync(string appId, CancellationToken cancellationToken)
    {
        try
        {
            var docs = await _store.GetAllForQueryAsync(appId, sourceId: null, asOf: null, cancellationToken)
                .ConfigureAwait(false);
            var entries = docs
                .Where(d => !GlobalWikiCatalog.IsReservedDocument(d.DocumentId))
                .OrderBy(d => d.DocumentId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"# {GlobalWikiCatalog.Title}");
            sb.AppendLine();
            sb.AppendLine($"_Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · {entries.Count} document(s)_");
            sb.AppendLine();
            sb.AppendLine(
                "Each entry is an LLM digest (keywords + up to 6 lines) that highlights rules from ticket comments.");
            sb.AppendLine();

            foreach (var doc in entries)
            {
                var heading = string.IsNullOrWhiteSpace(doc.Title) ? doc.DocumentId : $"{doc.DocumentId} — {doc.Title}";
                sb.Append("## ").AppendLine(heading);
                if (!string.IsNullOrWhiteSpace(doc.SourceId))
                    sb.Append("Source: ").AppendLine(doc.SourceId);

                var digest = string.IsNullOrWhiteSpace(doc.Summary)
                    ? GlobalWikiDigestGenerator.BuildFallbackDigest(doc.DocumentId, doc.Title, doc.Content)
                    : doc.Summary.Trim();
                sb.AppendLine(digest);
                sb.AppendLine();
            }

            await _store.UpsertAsync(
                appId,
                GlobalWikiCatalog.DocumentId,
                new GlobalWikiUpsertRequest
                {
                    Title = GlobalWikiCatalog.Title,
                    Content = sb.ToString().TrimEnd() + "\n",
                    Summary = $"Catalog of {entries.Count} documents with keyword digests.",
                    SourceId = "wiki:catalog",
                    Slug = "wiki-catalog",
                    Overwrite = true
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh global wiki catalog for {AppId}", appId);
        }
    }

    private async Task<(bool Refreshed, int PairCount)> RefreshGlossaryAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        try
        {
            var docs = await _store
                .GetAllForQueryAsync(appId, sourceId: null, asOf: null, cancellationToken)
                .ConfigureAwait(false);

            var existing = await _store
                .GetAsync(appId, GlobalWikiCatalog.GlossaryDocumentId, cancellationToken)
                .ConfigureAwait(false);
            var (manual, _) = GlobalWikiAliasLexicon.ParseGlossarySections(existing?.Content);

            var harvested = GlobalWikiAliasLexicon.HarvestPairsFromDocuments(docs);
            var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in harvested)
            {
                if (manual.ContainsKey(kv.Key))
                    continue;
                auto[kv.Key] = kv.Value;
            }

            var totalPairs = auto.Count + manual.Count;
            if (totalPairs == 0 && existing is null)
                return (Refreshed: false, PairCount: 0);

            var markdown = GlobalWikiAliasLexicon.BuildGlossaryMarkdown(auto, manual);
            var unchanged = existing is not null
                && string.Equals(existing.Content.Trim(), markdown.Trim(), StringComparison.Ordinal);
            if (unchanged)
                return (Refreshed: true, PairCount: totalPairs);

            await _store.UpsertAsync(
                appId,
                GlobalWikiCatalog.GlossaryDocumentId,
                new GlobalWikiUpsertRequest
                {
                    Title = GlobalWikiCatalog.GlossaryTitle,
                    Content = markdown,
                    Summary = $"Acronym glossary: {auto.Count} auto from digests, {manual.Count} manual.",
                    SourceId = "wiki:glossary",
                    Slug = "wiki-glossary",
                    Overwrite = true
                },
                cancellationToken).ConfigureAwait(false);

            GlobalWikiAliasLexicon.Invalidate(appId);
            return (Refreshed: true, PairCount: totalPairs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh global wiki glossary for {AppId}", appId);
            return (Refreshed: false, PairCount: 0);
        }
    }

    private static GlobalWikiDocumentSummary ToSummary(GlobalWikiDocument d) =>
        new()
        {
            DocumentId = d.DocumentId,
            Slug = d.Slug,
            Title = d.Title,
            Summary = d.Summary,
            SourceId = d.SourceId,
            RevisionId = d.RevisionId,
            Status = d.Status,
            ValidFrom = d.ValidFrom,
            ValidTo = d.ValidTo,
            UpdatedAt = d.UpdatedAt
        };

    private static string ResolvePackContent(GlobalWikiDocument doc, bool catalogIsPrimary, bool digestOnly)
    {
        if (GlobalWikiCatalog.IsCatalogDocument(doc.DocumentId))
        {
            if (catalogIsPrimary)
                return doc.Content;

            var pointer = string.IsNullOrWhiteSpace(doc.Summary)
                ? "Knowledge catalog overview (digests of ingested documents)."
                : doc.Summary.Trim();
            return pointer + "\n\n_(Ask specifically for the knowledge catalog to load the full digest index.)_";
        }

        if (!digestOnly)
            return doc.Content;

        // Dynamic context discovery: inject digests, hydrate full body via wiki_search when needed.
        if (!string.IsNullOrWhiteSpace(doc.Summary))
        {
            var title = string.IsNullOrWhiteSpace(doc.Title) ? doc.DocumentId : doc.Title.Trim();
            return $"### {title} (`{doc.DocumentId}`)\n{doc.Summary.Trim()}";
        }

        var excerpt = doc.Content ?? string.Empty;
        if (excerpt.Length > 400)
            excerpt = excerpt[..400] + "…";
        return $"### {doc.DocumentId}\n{excerpt}";
    }

    private static string BuildIndex(IReadOnlyList<GlobalWikiDocument> docs)
    {
        if (docs.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            docs.OrderByDescending(d => d.UpdatedAt)
                .Select(d =>
                {
                    var title = string.IsNullOrWhiteSpace(d.Title) ? d.Slug : d.Title;
                    var summary = string.IsNullOrWhiteSpace(d.Summary) ? string.Empty : $" — {FirstLine(d.Summary)}";
                    return $"- [{title}](pages/{d.Slug}.md){summary}";
                }));
    }

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        return line.Length <= 160 ? line : line[..160].TrimEnd() + "…";
    }
}
