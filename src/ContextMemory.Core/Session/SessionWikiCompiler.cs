using System.Text;

namespace ContextMemory.Core.Session;

public sealed class WikiCompileResult
{
    public required string Content { get; init; }
    public int CharCount { get; init; }
    public int IncludedPages { get; init; }
    public int TotalPages { get; init; }
    public bool Truncated { get; init; }
}

public static class SessionWikiCompiler
{
    private const string EmptyWiki = "(session wiki still empty)";
    private const string IndexTruncatedNote = "\n\n_(… index truncated — more pages on disk)_";
    private const string PageTruncatedSuffix = "\n\n_(… truncated)_";

    public static WikiCompileResult Compile(
        SessionSnapshot snapshot,
        string? userQuery,
        int budgetChars,
        bool includeIndex = true)
    {
        budgetChars = Math.Max(256, budgetChars);

        var hasRealIndex = includeIndex && !SessionWikiHelpers.IsPlaceholderIndex(snapshot.IndexMd);
        var logTail = includeIndex && SessionWikiHelpers.HasMeaningfulLog(snapshot.LogMd)
            ? SessionWikiHelpers.ExtractRecentLogTail(snapshot.LogMd, Math.Min(budgetChars / 3, 4000))
            : string.Empty;

        if (!hasRealIndex && snapshot.Pages.Count == 0 && string.IsNullOrEmpty(logTail))
        {
            return new WikiCompileResult
            {
                Content = EmptyWiki,
                CharCount = EmptyWiki.Length,
                IncludedPages = 0,
                TotalPages = 0,
                Truncated = false
            };
        }

        var sb = new StringBuilder();
        var remaining = budgetChars;
        var truncated = false;
        var included = 0;
        var totalPages = snapshot.Pages.Count;

        if (hasRealIndex)
        {
            var indexBlock = $"## Index\n{snapshot.IndexMd.Trim()}";
            if (indexBlock.Length > remaining)
            {
                indexBlock = indexBlock[..Math.Max(0, remaining - IndexTruncatedNote.Length)] + IndexTruncatedNote;
                truncated = true;
            }

            sb.Append(indexBlock);
            remaining -= indexBlock.Length;
        }

        if (!string.IsNullOrEmpty(logTail) && remaining > 0)
        {
            var logBlock = $"## Recent chronology\n{logTail}";
            if (logBlock.Length > remaining)
            {
                logBlock = logBlock[..Math.Max(0, remaining - PageTruncatedSuffix.Length)] + PageTruncatedSuffix;
                truncated = true;
            }

            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(logBlock);
            remaining -= logBlock.Length;
        }

        if (remaining <= 0 || snapshot.Pages.Count == 0)
        {
            if (!includeIndex && sb.Length == 0 && snapshot.Pages.Count > 0)
                truncated = true;

            return BuildResult(sb, included, totalPages, truncated);
        }

        var queryTokens = Tokenize(userQuery);
        var ranked = snapshot.Pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => new PageCandidate(
                p.Key,
                p.Value,
                ScorePage(p.Key, p.Value, snapshot.IndexMd, queryTokens, snapshot.PageLastModified)))
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Content.Length)
            .ToList();

        foreach (var page in ranked)
        {
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var header = $"## {page.Name}\n";
            var body = page.Content.Trim();
            var fullBlock = header + body;

            if (fullBlock.Length <= remaining)
            {
                if (sb.Length > 0)
                    sb.Append("\n\n");
                sb.Append(fullBlock);
                remaining -= fullBlock.Length;
                included++;
                continue;
            }

            var availableForBody = remaining - header.Length - PageTruncatedSuffix.Length;
            if (availableForBody > 80)
            {
                if (sb.Length > 0)
                    sb.Append("\n\n");
                sb.Append(header);
                sb.Append(body[..availableForBody].TrimEnd());
                sb.Append(PageTruncatedSuffix);
                remaining = 0;
                included++;
            }

            truncated = true;
            break;
        }

        if (included < ranked.Count)
            truncated = true;

        return BuildResult(sb, included, totalPages, truncated);
    }

    private static WikiCompileResult BuildResult(StringBuilder sb, int included, int totalPages, bool truncated)
    {
        var content = sb.Length == 0 ? EmptyWiki : sb.ToString();
        return new WikiCompileResult
        {
            Content = content,
            CharCount = content.Length,
            IncludedPages = included,
            TotalPages = totalPages,
            Truncated = truncated
        };
    }

    private static double ScorePage(
        string name,
        string content,
        string indexMd,
        HashSet<string> queryTokens,
        IReadOnlyDictionary<string, DateTimeOffset> lastModified)
    {
        var score = 0.0;

        if (queryTokens.Count > 0)
        {
            var haystack = $"{name} {content} {indexMd}".ToLowerInvariant();
            foreach (var token in queryTokens)
            {
                if (haystack.Contains(token, StringComparison.Ordinal))
                    score += 10;
            }
        }

        if (lastModified.TryGetValue(name, out var modified))
        {
            var ageHours = (DateTimeOffset.UtcNow - modified).TotalHours;
            score += Math.Max(0, 48 - ageHours) / 4;
        }

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

    private sealed record PageCandidate(string Name, string Content, double Score);
}
