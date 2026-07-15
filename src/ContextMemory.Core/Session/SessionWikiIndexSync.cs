using System.Text;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Session;

internal static partial class SessionWikiIndexSync
{
    public static string Reconcile(string sessionDir, string? proposedIndexMd)
    {
        HoistNestedPageFiles(sessionDir);
        var pages = CollectPageFiles(sessionDir);

        if (pages.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(proposedIndexMd) || SessionWikiHelpers.IsPlaceholderIndex(proposedIndexMd))
                return SessionDefaults.EmptyIndex.TrimEnd();

            return IndexOutOfSync(proposedIndexMd, pages)
                ? SessionDefaults.EmptyIndex.TrimEnd()
                : proposedIndexMd.Trim();
        }

        if (string.IsNullOrWhiteSpace(proposedIndexMd)
            || SessionWikiHelpers.IsPlaceholderIndex(proposedIndexMd)
            || IndexOutOfSync(proposedIndexMd, pages))
            return BuildFromPages(sessionDir, pages);

        return proposedIndexMd.Trim();
    }

    public static bool RepairSessionLayout(string sessionDir)
    {
        HoistNestedPageFiles(sessionDir);

        var indexPath = Path.Combine(sessionDir, "index.md");
        var current = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;
        var reconciled = Reconcile(sessionDir, current);
        if (string.Equals(current.Trim(), reconciled.Trim(), StringComparison.Ordinal))
            return false;

        File.WriteAllText(indexPath, reconciled.TrimEnd() + "\n");
        return true;
    }

    public static void HoistNestedPageFiles(string sessionDir)
    {
        var pagesDir = Path.Combine(sessionDir, "pages");
        if (!Directory.Exists(pagesDir))
            return;

        foreach (var file in Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.AllDirectories))
        {
            var parentDir = Path.GetDirectoryName(file);
            if (string.Equals(parentDir, pagesDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(pagesDir, Path.GetFileName(file));
            if (File.Exists(dest))
                File.Delete(file);
            else
                File.Move(file, dest);
        }

        foreach (var subdir in Directory.EnumerateDirectories(pagesDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(subdir).Any())
                    Directory.Delete(subdir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    public static string BuildFromPagesDirectory(string sessionDir)
    {
        HoistNestedPageFiles(sessionDir);
        var pages = CollectPageFiles(sessionDir);
        return pages.Count == 0 ? SessionDefaults.EmptyIndex.TrimEnd() : BuildFromPages(sessionDir, pages);
    }

    private static string BuildFromPages(string sessionDir, IReadOnlyList<PageEntry> pages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Session index");
        sb.AppendLine();
        sb.AppendLine("| Página | Resumo |");
        sb.AppendLine("|---|---|");

        foreach (var page in pages.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var link = $"pages/{page.FileName}";
            var title = Path.GetFileNameWithoutExtension(page.FileName);
            sb.AppendLine($"| [{title}]({link}) | {page.Summary} |");
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IndexOutOfSync(string indexMd, IReadOnlyList<PageEntry> pages)
    {
        var existingFiles = pages.Select(p => p.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingStems = pages
            .Select(p => Path.GetFileNameWithoutExtension(p.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PageLinkRegex().Matches(indexMd))
        {
            var fileName = Path.GetFileName(match.Value.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName))
                continue;

            referencedStems.Add(Path.GetFileNameWithoutExtension(fileName));
            if (!existingFiles.Contains(fileName))
                return true;
        }

        foreach (Match match in BulletPageStemRegex().Matches(indexMd))
        {
            var stem = match.Groups[1].Value;
            if (string.IsNullOrEmpty(stem))
                continue;

            referencedStems.Add(stem);
            if (!existingStems.Contains(stem))
                return true;
        }

        if (referencedStems.Count > 0 && existingStems.Count != referencedStems.Count)
            return true;

        if (pages.Count > 0 && !PageLinkRegex().IsMatch(indexMd))
            return true;

        return false;
    }

    private static List<PageEntry> CollectPageFiles(string sessionDir)
    {
        var pagesDir = Path.Combine(sessionDir, "pages");
        if (!Directory.Exists(pagesDir))
            return [];

        var result = new List<PageEntry>();
        foreach (var path in Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var content = File.ReadAllText(path);
            result.Add(new PageEntry(fileName, ExtractSummary(content, fileName)));
        }

        return result;
    }

    private static string ExtractSummary(string content, string fileName)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                return trimmed.TrimStart('#').Trim();

            if (trimmed.Length > 0)
                return trimmed.Length <= 120 ? trimmed : trimmed[..117] + "...";
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    [GeneratedRegex(@"pages/[\w\-\.]+\.md", RegexOptions.IgnoreCase)]
    private static partial Regex PageLinkRegex();

    [GeneratedRegex(@"^\s*[\*\-]\s+\*\*([\w\-]+)\*\*", RegexOptions.Multiline)]
    private static partial Regex BulletPageStemRegex();

    private sealed record PageEntry(string FileName, string Summary);
}
