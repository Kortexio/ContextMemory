using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.GlobalWiki;

public sealed class GlobalWikiService
{
    public const int DefaultTopK = 5;
    public const int DefaultBudgetChars = 8_000;

    private readonly IGlobalWikiStore _store;

    public GlobalWikiService(IGlobalWikiStore store) => _store = store;

    public Task<GlobalWikiUpsertResult> UpsertAsync(
        string appId,
        string documentId,
        GlobalWikiUpsertRequest request,
        CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(appId, documentId, request, cancellationToken);

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
                    Slug = doc.Slug
                },
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new GlobalWikiBatchUpsertResult { Results = results };
    }

    public Task<bool> DeleteAsync(string appId, string documentId, CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(appId, documentId, cancellationToken);

    public async Task<GlobalWikiListResult> ListAsync(
        string appId,
        string? sourceId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

        var total = await _store.CountAsync(appId, sourceId, cancellationToken).ConfigureAwait(false);
        var docs = await _store.ListAsync(appId, sourceId, offset, limit, cancellationToken).ConfigureAwait(false);

        return new GlobalWikiListResult
        {
            Offset = offset,
            Limit = limit,
            Total = total,
            Documents = docs.Select(d => new GlobalWikiDocumentSummary
            {
                DocumentId = d.DocumentId,
                Slug = d.Slug,
                Title = d.Title,
                Summary = d.Summary,
                SourceId = d.SourceId,
                UpdatedAt = d.UpdatedAt
            }).ToList()
        };
    }

    public async Task<GlobalWikiQueryResult> QueryAsync(
        string appId,
        GlobalWikiQueryRequest request,
        int? defaultBudgetChars = null,
        CancellationToken cancellationToken = default)
    {
        var docs = await _store
            .GetAllForQueryAsync(appId, request.SourceId, cancellationToken)
            .ConfigureAwait(false);

        var topK = request.TopK > 0 ? Math.Min(request.TopK, 50) : DefaultTopK;
        var budget = request.BudgetChars > 0
            ? request.BudgetChars
            : defaultBudgetChars is > 0 ? defaultBudgetChars.Value : DefaultBudgetChars;

        var scored = ScoreMatches(docs, request.Query).Take(topK).ToList();
        var matches = scored
            .Select(m => new GlobalWikiMatch
            {
                DocumentId = m.Document.DocumentId,
                Slug = m.Document.Slug,
                Title = m.Document.Title,
                Score = m.Score,
                SourceId = m.Document.SourceId
            })
            .ToList();

        if (scored.Count == 0)
        {
            return new GlobalWikiQueryResult
            {
                CompiledMarkdown = string.Empty,
                CharCount = 0,
                IncludedDocuments = 0,
                TotalDocuments = docs.Count,
                Truncated = false,
                Matches = matches
            };
        }

        // Pack ONLY top-K matches — never the full corpus (index/filler pages would exhaust budget).
        var matchedDocs = scored.Select(s => s.Document).ToList();
        var pages = matchedDocs.ToDictionary(d => d.Slug, d => d.Content, StringComparer.OrdinalIgnoreCase);
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

        // Optional index of matches only, and only after bodies if budget remains.
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
            TotalDocuments = docs.Count,
            Truncated = truncated,
            Matches = matches
        };
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
                    var summary = string.IsNullOrWhiteSpace(d.Summary) ? string.Empty : $" — {d.Summary}";
                    return $"- [{title}](pages/{d.Slug}.md){summary}";
                }));
    }

    private static IEnumerable<(GlobalWikiDocument Document, double Score)> ScoreMatches(
        IReadOnlyList<GlobalWikiDocument> docs,
        string query)
    {
        var tokens = Tokenize(query);
        return docs
            .Select(d => (Document: d, Score: ScoreDocument(d, tokens)))
            .Where(x => tokens.Count == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Document.Content.Length);
    }

    private static double ScoreDocument(GlobalWikiDocument doc, HashSet<string> tokens)
    {
        var score = 0.0;
        if (tokens.Count > 0)
        {
            var identity = $"{doc.DocumentId} {doc.Slug} {doc.Title}".ToLowerInvariant();
            var body = $"{doc.Summary} {doc.Content} {doc.SourceId}".ToLowerInvariant();
            foreach (var token in tokens)
            {
                // Identity hits must outrank generic keyword noise across hundreds of tickets.
                if (identity.Contains(token, StringComparison.Ordinal))
                    score += 100;
                else if (body.Contains(token, StringComparison.Ordinal))
                    score += 10;
            }
        }

        var ageHours = (DateTimeOffset.UtcNow - doc.UpdatedAt).TotalHours;
        score += Math.Max(0, 48 - ageHours) / 4;
        return score;
    }

    private static HashSet<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);
    }
}
