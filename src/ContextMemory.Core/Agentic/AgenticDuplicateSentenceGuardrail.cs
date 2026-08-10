using ContextMemory.Core.Localization;
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

        var sentences = SplitSentences(finalAnswer);
        if (sentences.Count < 2)
            return false;

        for (var i = 1; i < sentences.Count; i++)
        {
            var prev = Normalize(sentences[i - 1]);
            var cur = Normalize(sentences[i]);
            if (prev.Length < 20 || cur.Length < 20)
                continue;

            if (string.Equals(prev, cur, StringComparison.Ordinal)
                || (prev.Length > 40 && cur.Contains(prev, StringComparison.Ordinal))
                || (cur.Length > 40 && prev.Contains(cur, StringComparison.Ordinal)))
            {
                feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                    ?? TenantLocale.Select(
                        runtimeConfig.DefaultLanguage,
                        "Rejected: duplicate sentences detected.",
                        "Rejeitado: frases duplicadas detectadas.");
                return true;
            }
        }

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
