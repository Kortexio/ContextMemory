namespace ContextMemory.Core.Session;

internal static class SessionWikiPagePaths
{
    public static bool TryNormalize(string? rawPath, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        var relative = rawPath.Replace('\\', '/').Trim();
        while (relative.StartsWith("./", StringComparison.Ordinal))
            relative = relative[2..];

        relative = relative.TrimStart('/');

        const string duplicatePrefix = "pages/pages/";
        while (relative.StartsWith(duplicatePrefix, StringComparison.OrdinalIgnoreCase))
            relative = "pages/" + relative[duplicatePrefix.Length..];

        if (!relative.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
            relative = "pages/" + relative.TrimStart('/');

        var fileName = Path.GetFileName(relative);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            fileName += ".md";

        relativePath = "pages/" + fileName;
        return true;
    }
}
