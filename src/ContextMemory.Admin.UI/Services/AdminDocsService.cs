using System.Globalization;
using Microsoft.Extensions.Options;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminDocsService(
    IOptions<AdminUiOptions> options,
    AdminDocsMarkdownRenderer markdown) : IAdminDocsService
{
    public IReadOnlyList<AdminDocEntry> ListDocuments()
    {
        var root = ResolveDocsRoot();
        if (root is null)
            return [];

        return Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var slug = Path.GetFileNameWithoutExtension(fileName);
                if (slug.Equals("README", StringComparison.OrdinalIgnoreCase))
                    slug = "index";
                return new AdminDocEntry(slug, Humanize(slug), fileName);
            })
            .OrderBy(e => e.Slug.Equals("index", StringComparison.OrdinalIgnoreCase) ? "" : e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AdminDocContent?> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        var root = ResolveDocsRoot();
        if (root is null)
            return null;

        var readme = Path.Combine(root, "README.md");
        if (!File.Exists(readme))
            return null;

        var text = await File.ReadAllTextAsync(readme, cancellationToken).ConfigureAwait(false);
        var entry = new AdminDocEntry("index", AdminDocsMarkdownRenderer.TitleFromMarkdown(text, "Docs"), "README.md");
        var html = markdown.ToHtml(text, BuildFileMap());
        return new AdminDocContent(entry, html);
    }

    public async Task<AdminDocContent?> GetDocumentAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        if (slug.Equals("index", StringComparison.OrdinalIgnoreCase)
            || slug.Equals("readme", StringComparison.OrdinalIgnoreCase))
            return await GetIndexAsync(cancellationToken).ConfigureAwait(false);

        var root = ResolveDocsRoot();
        if (root is null)
            return null;

        // Prevent path traversal
        if (slug.Contains("..", StringComparison.Ordinal) || slug.Contains('/') || slug.Contains('\\'))
            return null;

        var path = Path.Combine(root, $"{slug}.md");
        if (!File.Exists(path))
            return null;

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var fileName = Path.GetFileName(path);
        var entry = new AdminDocEntry(slug, AdminDocsMarkdownRenderer.TitleFromMarkdown(text, Humanize(slug)), fileName);
        var html = markdown.ToHtml(text, BuildFileMap());
        return new AdminDocContent(entry, html);
    }

    private Dictionary<string, string> BuildFileMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ListDocuments())
        {
            map[entry.FileName] = entry.Slug.Equals("index", StringComparison.OrdinalIgnoreCase)
                ? "" // rewritten specially for README
                : entry.Slug;
        }

        // Ensure README maps correctly even if ListDocuments renamed slug
        map["README.md"] = "";
        return map;
    }

    private string? ResolveDocsRoot()
    {
        var configured = options.Value.DocsPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);
        return null;
    }

    private static string Humanize(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' ').Replace('_', ' '));
}
