using System.Text.RegularExpressions;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.WebSearch;

public sealed partial class HeuristicFreshnessDetector
{
    private static readonly string[] FreshnessSignals =
    [
        "hoje", "agora", "actual", "atual", "recente", "2025", "2026",
        "última lei", "ultima lei", "notícias", "noticias", "o que mudou",
        "estado actual", "estado atual", "últimas", "ultimas",
        "buscar na web", "pesquisar", "pesquise", "na internet", "em tempo real"
    ];

    public WebSearchDecision Evaluate(string userQuery, SessionSnapshot snapshot)
    {
        if (LooksHistoricalOnly(userQuery))
            return WebSearchDecision.Skip("historical", "heuristic");

        if (ContainsFreshnessSignal(userQuery))
            return WebSearchDecision.Search(userQuery, "heuristic");

        if (HasWikiGapForQuery(userQuery, snapshot))
            return WebSearchDecision.Search(userQuery, "heuristic");

        return WebSearchDecision.Skip("heuristic", "heuristic");
    }

    private static bool LooksHistoricalOnly(string query)
    {
        if (HistoricalYearRegex().IsMatch(query) && !ContainsFreshnessSignal(query))
            return true;

        return query.Contains("século", StringComparison.OrdinalIgnoreCase)
               || query.Contains("seculo", StringComparison.OrdinalIgnoreCase)
               || query.Contains("antig", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsFreshnessSignal(string query)
    {
        foreach (var signal in FreshnessSignals)
        {
            if (query.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasWikiGapForQuery(string query, SessionSnapshot snapshot)
    {
        if (snapshot.Pages.Count == 0)
            return false;

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return false;

        var pageStems = snapshot.Pages.Keys
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var overlap = tokens.Count(t => pageStems.Any(stem =>
            !string.IsNullOrEmpty(stem)
            && (stem.Contains(t, StringComparison.OrdinalIgnoreCase)
                || t.Contains(stem, StringComparison.OrdinalIgnoreCase))));

        return overlap == 0 && tokens.Count >= 2;
    }

    private static HashSet<string> Tokenize(string query) =>
        query.Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 4)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\b(1[0-9]{3}|200[0-9]|201[0-9]|202[0-3])\b")]
    private static partial Regex HistoricalYearRegex();
}
