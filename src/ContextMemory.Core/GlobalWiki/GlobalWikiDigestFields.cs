using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.GlobalWiki;

/// <summary>Parsed structured fields from LLM wiki digests (<c>Summary</c>).</summary>
public static class GlobalWikiDigestFields
{
    private static readonly Regex FieldLine = new(
        @"^(Keywords|Aliases|Questions):\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> ExtractQuestionPhrases(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return [];

        var phrases = new List<string>();
        foreach (var raw in summary.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = FieldLine.Match(raw.Trim());
            if (!match.Success || !match.Groups[1].Value.Equals("Questions", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var part in match.Groups[2].Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = GlobalWikiScoring.Tokenize(part);
                if (normalized.Count >= 2)
                    phrases.Add(string.Join(' ', normalized.OrderBy(t => t, StringComparer.Ordinal)));
            }
        }

        return phrases;
    }

    public static string DigestIndexText(GlobalWikiDocument doc) =>
        $"{doc.DocumentId} {doc.Slug} {doc.Title} {doc.Summary} {doc.SourceId}";
}
