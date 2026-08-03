using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.GlobalWiki;

/// <summary>Shared lexical scoring for Global Wiki search (File + Postgres fallback).</summary>
public static class GlobalWikiScoring
{
    public static IEnumerable<(GlobalWikiDocument Document, double Score)> ScoreMatches(
        IReadOnlyList<GlobalWikiDocument> docs,
        string query)
    {
        var tokens = Tokenize(query);
        var phrase = string.Join(' ', tokens);
        return docs
            .Select(d => (Document: d, Score: ScoreDocument(d, tokens, phrase)))
            .Where(x => tokens.Count == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Document.Content.Length);
    }

    public static double ScoreDocument(GlobalWikiDocument doc, HashSet<string> tokens, string phrase)
    {
        var score = 0.0;
        if (tokens.Count > 0)
        {
            var identity = $"{doc.DocumentId} {doc.Slug} {doc.Title}".ToLowerInvariant();
            var summary = (doc.Summary ?? string.Empty).ToLowerInvariant();
            var body = $"{doc.Summary} {doc.Content} {doc.SourceId}".ToLowerInvariant();

            // Phrase boost when the full query appears.
            if (phrase.Length >= 4 && body.Contains(phrase, StringComparison.Ordinal))
                score += 40;

            foreach (var token in tokens)
            {
                if (identity.Contains(token, StringComparison.Ordinal))
                    score += 100;
                else if (summary.Contains(token, StringComparison.Ordinal))
                {
                    // Digest keywords / summary hits outrank generic body noise.
                    score += summary.Contains("keywords:", StringComparison.Ordinal)
                        ? 35
                        : 20;
                }
                else if (body.Contains(token, StringComparison.Ordinal))
                    score += 10;
            }

            if (GlobalWikiCatalog.IsCatalogDocument(doc.DocumentId))
                score += 15;
        }

        var ageHours = (DateTimeOffset.UtcNow - doc.UpdatedAt).TotalHours;
        score += Math.Max(0, 48 - ageHours) / 4;
        return score;
    }

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
