namespace ContextMemory.Core.Session;

internal static class SessionWikiHelpers
{
    public static bool IsPlaceholderIndex(string? indexMd) =>
        string.IsNullOrWhiteSpace(indexMd)
        || indexMd.Contains("_No pages yet", StringComparison.Ordinal)
        || indexMd.Contains("_No content yet", StringComparison.Ordinal)
        || indexMd.Contains("_Nenhuma página ainda", StringComparison.Ordinal)
        || indexMd.Contains("_Nenhum conteúdo ainda", StringComparison.Ordinal);

    public static bool HasMeaningfulLog(string? logMd)
    {
        if (string.IsNullOrWhiteSpace(logMd))
            return false;

        var trimmed = logMd.Trim();
        if (trimmed.Equals(SessionDefaults.EmptyLog.Trim(), StringComparison.Ordinal))
            return false;

        return trimmed.Contains("## [", StringComparison.Ordinal);
    }

    public static string ExtractRecentLogTail(string logMd, int maxChars)
    {
        maxChars = Math.Max(256, maxChars);
        var entries = logMd
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("## [", StringComparison.Ordinal))
            .ToList();

        if (entries.Count == 0)
            return string.Empty;

        var selected = new List<string>();
        var length = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var line = entries[i];
            var added = line.Length + (selected.Count > 0 ? 1 : 0);
            if (length + added > maxChars && selected.Count > 0)
                break;

            selected.Insert(0, line);
            length += added;
        }

        return string.Join('\n', selected);
    }

    public static long GetDirectorySizeBytes(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // best effort
            }
        }

        return total;
    }

    public static int CountWikiPages(string sessionPath)
    {
        var pagesDir = Path.Combine(sessionPath, "pages");
        if (!Directory.Exists(pagesDir))
            return 0;

        return Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.TopDirectoryOnly).Count();
    }
}
