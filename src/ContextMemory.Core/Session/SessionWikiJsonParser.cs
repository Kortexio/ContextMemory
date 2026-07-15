using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Session;

public static partial class SessionWikiJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();

        var fence = JsonFenceRegex().Match(trimmed);
        if (fence.Success)
            return fence.Groups["json"].Value.Trim();

        var objectMatch = JsonObjectRegex().Match(trimmed);
        if (objectMatch.Success)
            return objectMatch.Value;

        return trimmed;
    }

    public static SessionWikiUpdate? TryParseUpdate(string raw)
    {
        var payload = ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ParseRoot(doc.RootElement, includeDeletePages: false);
        }
        catch
        {
            return null;
        }
    }

    public static SessionWikiUpdate? TryParseCompaction(string raw)
    {
        var payload = ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ParseRoot(doc.RootElement, includeDeletePages: true);
        }
        catch
        {
            return null;
        }
    }

    private static SessionWikiUpdate? ParseRoot(JsonElement root, bool includeDeletePages)
    {
        var pages = ParsePages(root);
        var deletePages = includeDeletePages ? ParseDeletePages(root) : [];

        string? indexMd = null;
        if (root.TryGetProperty("index_md", out var idx) && idx.ValueKind == JsonValueKind.String)
            indexMd = idx.GetString();

        string? logEntry = null;
        if (root.TryGetProperty("log_entry", out var log) && log.ValueKind == JsonValueKind.String)
            logEntry = log.GetString();

        if (logEntry is null && indexMd is null && pages.Count == 0 && deletePages.Count == 0)
            return null;

        return new SessionWikiUpdate
        {
            LogEntry = logEntry,
            IndexMd = indexMd,
            Pages = pages,
            DeletePages = deletePages
        };
    }

    private static List<SessionPageUpdate> ParsePages(JsonElement root)
    {
        var pages = new List<SessionPageUpdate>();
        if (!root.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array)
            return pages;

        foreach (var item in pagesEl.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
            var content = item.TryGetProperty("content", out var c) ? c.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path) && content is not null)
                pages.Add(new SessionPageUpdate { Path = path, Content = content });
        }

        return pages;
    }

    private static List<string> ParseDeletePages(JsonElement root)
    {
        var deletePages = new List<string>();
        if (!root.TryGetProperty("delete_pages", out var delEl) || delEl.ValueKind != JsonValueKind.Array)
            return deletePages;

        foreach (var item in delEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                deletePages.Add(item.GetString()!);
        }

        return deletePages;
    }

    public static SessionWikiUpdate CreateFallbackLogEntry(string? reason = null, string? language = null)
    {
        var suffix = string.IsNullOrWhiteSpace(reason)
            ? LlmPrompts.WikiLogDefaultSuffix(language)
            : reason;
        return new SessionWikiUpdate
        {
            LogEntry = $"## [{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}] {suffix}"
        };
    }

    [GeneratedRegex(@"```(?:json)?\s*(?<json>\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex JsonObjectRegex();
}
