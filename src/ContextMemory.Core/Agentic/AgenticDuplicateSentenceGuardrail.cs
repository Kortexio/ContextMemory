using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticDuplicateSentenceGuardrail
{
    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!ContainsDuplicateContent(finalAnswer))
            return false;

        feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        return true;
    }

    /// <summary>
    /// Detect both adjacent duplicates and model degeneration where a whole section is emitted
    /// again later in the same answer. Long exact sentences are unlikely to recur legitimately.
    /// </summary>
    public static bool ContainsDuplicateContent(string? finalAnswer)
    {
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var sentences = SplitSentences(finalAnswer);
        if (sentences.Count < 2)
            return false;

        var seenLong = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < sentences.Count; i++)
        {
            var prev = Normalize(sentences[i - 1]);
            var cur = Normalize(sentences[i]);
            if (prev.Length >= 20 && cur.Length >= 20
                && (string.Equals(prev, cur, StringComparison.Ordinal)
                    || (prev.Length > 40 && cur.Contains(prev, StringComparison.Ordinal))
                    || (cur.Length > 40 && prev.Contains(cur, StringComparison.Ordinal))))
            {
                return true;
            }

            // Non-consecutive duplicate: catches repeated sections/answers without flagging
            // short labels and headings that can legitimately recur.
            if (cur.Length >= 50 && !seenLong.Add(cur))
                return true;
        }

        // Include the first sentence in the global check (the loop above starts at index 1).
        var first = Normalize(sentences[0]);
        if (first.Length >= 50
            && sentences.Skip(1).Select(Normalize).Contains(first, StringComparer.Ordinal))
            return true;

        return false;
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = text.Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Where(p => p.Length > 0).ToList();
    }

    private static string Normalize(string s) =>
        string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
