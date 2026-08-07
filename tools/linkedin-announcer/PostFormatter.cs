using System.Text;
using System.Text.RegularExpressions;

namespace LinkedInAnnouncer;

public static partial class PostFormatter
{
    private const int MaxLength = 2800;

    private static readonly string[] Hooks =
    [
        "Your coding agent forgets. Fix that in five minutes.",
        "Agent memory you can open like a wiki.",
        "Not a vector black box — markdown you can edit.",
        "Not classic RAG. Memory as a tool in the agent loop.",
        "One OpenAI URL: recall, tools, and HITL."
    ];

    public static string FormatReleasePost(string tagName, string releaseBody, string repoUrl, string? hook = null)
    {
        hook ??= Hooks[Math.Abs(tagName.GetHashCode(StringComparison.Ordinal)) % Hooks.Length];
        var notesUrl = $"{repoUrl.TrimEnd('/')}/releases/tag/{tagName}";
        var why = SummarizeBody(releaseBody);
        var hashtags = "#dotnet #opensource #AI #LLM #agents #MCP #ragalternative";

        // LinkedIn: short hook + 1–2 lines + one CTA (MESSAGING.md voice).
        var sb = new StringBuilder();
        sb.AppendLine(hook);
        sb.AppendLine();
        sb.AppendLine($"ContextMemory {tagName} is out — public beta.");
        if (!string.IsNullOrWhiteSpace(why))
        {
            sb.AppendLine(why);
        }
        else
        {
            sb.AppendLine("Drop-in /v1 chat with wiki memory, MCP, and HITL. Self-host or Cloud.");
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
            return string.Empty;

        var lines = releaseBody
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(CleanMarkdownLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !IsNoiseLine(l))
            .Take(2)
            .ToList();

        if (lines.Count == 0)
            return string.Empty;

        // Prefer a single short paragraph over a bullet dump.
        var joined = string.Join(' ', lines.Select(l => l.TrimStart('•', ' ').Trim()));
        if (joined.Length > 220)
            joined = joined[..217].TrimEnd() + "…";
        return joined;
    }

    private static bool IsNoiseLine(string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.StartsWith("try it", StringComparison.Ordinal))
            return true;
        if (lower.StartsWith("repo:", StringComparison.Ordinal))
            return true;
        if (lower.StartsWith("full notes", StringComparison.Ordinal))
            return true;
        if (lower.Contains("http://", StringComparison.Ordinal) || lower.Contains("https://", StringComparison.Ordinal))
            return true;
        if (lower is "what's in this beta" or "what shipped" or "notes" or "release notes")
            return true;
        if (lower.StartsWith("contextmemory public beta", StringComparison.Ordinal))
            return true;
        if (lower.StartsWith("contextmemory v", StringComparison.Ordinal))
            return true;
        // Skip bullets that are just feature laundry lists for social.
        if (line.StartsWith('•'))
            return true;
        return false;
    }

    private static string CleanMarkdownLine(string line)
    {
        line = HeadingRegex().Replace(line, "");
        line = LinkRegex().Replace(line, "$1");
        line = BoldRegex().Replace(line, "$1");
        line = InlineCodeRegex().Replace(line, "$1");
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

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();
}
