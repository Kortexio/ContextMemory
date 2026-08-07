using System.Text;
using System.Text.RegularExpressions;

namespace LinkedInAnnouncer;

public static partial class PostFormatter
{
    private const int MaxLength = 2800;

    private static readonly string[] Hooks =
    [
        "Your agent forgets. This release helps.",
        "Memory you can open like a wiki.",
        "Not a vector black box.",
        "Not classic RAG — agentic memory.",
        "Same URL: recall and action."
    ];

    public static string FormatReleasePost(string tagName, string releaseBody, string repoUrl, string? hook = null)
    {
        hook ??= Hooks[Math.Abs(tagName.GetHashCode()) % Hooks.Length];
        var notesUrl = $"{repoUrl.TrimEnd('/')}/releases/tag/{tagName}";
        var summary = SummarizeBody(releaseBody);
        var hashtags = "#dotnet #opensource #AI #LLM #agents #MCP";

        var sb = new StringBuilder();
        sb.AppendLine(hook);
        sb.AppendLine();
        sb.AppendLine($"ContextMemory {tagName} is out.");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine();
            sb.AppendLine(summary);
        }
        sb.AppendLine();
        sb.AppendLine($"Try it: {notesUrl}");
        sb.AppendLine();
        sb.Append(hashtags);

        var text = sb.ToString();
        if (text.Length <= MaxLength)
            return text;

        var reserve = hashtags.Length + notesUrl.Length + 40;
        var keep = Math.Max(0, MaxLength - reserve);
        return text[..keep].TrimEnd() + "…\n\nTry it: " + notesUrl + "\n\n" + hashtags;
    }

    private static string SummarizeBody(string releaseBody)
    {
        if (string.IsNullOrWhiteSpace(releaseBody))
            return "What changed: see full release notes.";

        var lines = releaseBody
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(CleanMarkdownLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(6)
            .ToList();

        if (lines.Count == 0)
            return "What changed: see full release notes.";

        return string.Join('\n', lines);
    }

    private static string CleanMarkdownLine(string line)
    {
        line = HeadingRegex().Replace(line, "");
        line = LinkRegex().Replace(line, "$1");
        line = BoldRegex().Replace(line, "$1");
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            line = "• " + line[2..];
        return line.Trim();
    }

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();
}
