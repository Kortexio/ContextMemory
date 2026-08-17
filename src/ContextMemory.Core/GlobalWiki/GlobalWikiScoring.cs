using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.GlobalWiki;

/// <summary>Shared lexical scoring for Global Wiki search (File + Postgres fallback).</summary>
public static class GlobalWikiScoring
{
    public static IEnumerable<(GlobalWikiDocument Document, double Score)> ScoreMatches(
        IReadOnlyList<GlobalWikiDocument> docs,
        string query,
        GlobalWikiAliasLexicon? lexicon = null)
    {
        var expansion = (lexicon ?? GlobalWikiAliasLexicon.Empty).Expand(query);
        return docs
            .Select(d => (Document: d, Score: ScoreDocument(d, expansion)))
            .Where(x => expansion.Groups.Count == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Document.Content.Length);
    }

    public static double ScoreDocument(GlobalWikiDocument doc, GlobalWikiQueryExpansion expansion)
    {
        var score = 0.0;
        if (expansion.Groups.Count > 0)
        {
            var identity = $"{doc.DocumentId} {doc.Slug} {doc.Title}".ToLowerInvariant();
            var summary = (doc.Summary ?? string.Empty).ToLowerInvariant();
            var body = $"{doc.Summary} {doc.Content} {doc.SourceId}".ToLowerInvariant();

            if (expansion.OriginalPhrase.Length >= 4
                && body.Contains(expansion.OriginalPhrase, StringComparison.Ordinal))
                score += 40;

            foreach (var group in expansion.Groups)
            {
                if (group.Hits(identity))
                    score += 100;
                else if (group.Hits(summary))
                {
                    score += summary.Contains("keywords:", StringComparison.Ordinal)
                        ? 35
                        : 20;
                }
                else if (group.Hits(body))
                    score += 10;
            }

            if (GlobalWikiCatalog.IsCatalogDocument(doc.DocumentId))
                score += 15;
        }

        var ageHours = (DateTimeOffset.UtcNow - doc.UpdatedAt).TotalHours;
        score += Math.Max(0, 48 - ageHours) / 4;
        return score;
    }

    public static double ScoreDocument(GlobalWikiDocument doc, HashSet<string> tokens, string phrase) =>
        ScoreDocument(
            doc,
            new GlobalWikiQueryExpansion
            {
                OriginalQuery = phrase,
                OriginalPhrase = phrase,
                Groups = tokens.Select(t => new GlobalWikiSynonymGroup
                {
                    Canonical = t,
                    Acronym = string.Empty,
                    ExpansionPhrase = string.Empty,
                    ExpansionIndexTokens = [],
                    IndexTokens = [t]
                }).ToList()
            });

    public static HashSet<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split([' ', '\t', '\n', '\r', ',', '.', ':', ';', '/', '|', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);
    }
}
