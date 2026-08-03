using System.Text.RegularExpressions;
using Markdig;

namespace ContextMemory.Admin.UI.Services;

public sealed partial class AdminDocsMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseBootstrap()
        .Build();

    public string ToHtml(string markdown, IReadOnlyDictionary<string, string> fileNameToSlug)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var html = Markdown.ToHtml(markdown, Pipeline);
        return RewriteInternalLinks(html, fileNameToSlug);
    }

    public static string TitleFromMarkdown(string markdown, string fallback)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }

        return fallback;
    }

    private static string RewriteInternalLinks(string html, IReadOnlyDictionary<string, string> fileNameToSlug)
    {
        return DocLinkPattern().Replace(html, match =>
        {
            var href = match.Groups["href"].Value;
            var fileName = Path.GetFileName(href.Split('#', '?')[0].TrimStart('.', '/'));
            if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            if (!fileNameToSlug.TryGetValue(fileName, out var slug))
            {
                slug = Path.GetFileNameWithoutExtension(fileName);
                if (slug.Equals("README", StringComparison.OrdinalIgnoreCase))
                    return "href=\"/docs\"";
            }

            if (string.IsNullOrEmpty(slug) || slug.Equals("index", StringComparison.OrdinalIgnoreCase))
                return "href=\"/docs\"";

            return $"href=\"/docs/{slug}\"";
        });
    }

    [GeneratedRegex("href=\"(?<href>[^\"]+\\.md[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DocLinkPattern();
}
