using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.GlobalWiki;

public static partial class GlobalWikiSlug
{
    public static string FromDocumentId(string documentId, string? explicitSlug = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitSlug)
            && SessionWikiPagePaths.TryNormalize(explicitSlug, out var normalizedExplicit))
        {
            return Path.GetFileNameWithoutExtension(normalizedExplicit);
        }

        var raw = documentId.Trim().ToLowerInvariant();
        raw = InvalidSlugChars().Replace(raw, "-");
        raw = CollapseDashes().Replace(raw, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(raw))
            raw = "doc";

        if (raw.Length > 100)
            raw = raw[..100].TrimEnd('-');

        if (SessionWikiPagePaths.TryNormalize(raw + ".md", out var path))
            return Path.GetFileNameWithoutExtension(path);

        return raw;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ExtractTitle(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
            return fallback;

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }

        return fallback;
    }

    public static string ExtractSummary(string content, string? explicitSummary)
    {
        if (!string.IsNullOrWhiteSpace(explicitSummary))
            return explicitSummary.Trim();

        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var text = content
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .FirstOrDefault() ?? string.Empty;

        return text.Length <= 400 ? text : text[..400].TrimEnd() + "…";
    }

    [GeneratedRegex(@"[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidSlugChars();

    [GeneratedRegex(@"-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseDashes();
}
