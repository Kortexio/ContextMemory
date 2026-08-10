using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticRelevanceGuardrail
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "o", "os", "as", "de", "da", "do", "das", "dos", "e", "ou", "um", "uma", "em", "no", "na",
        "the", "and", "or", "of", "to", "in", "for", "is", "are", "on", "at", "by", "with", "an", "be"
    };

    public static bool TryGetRejectionFeedback(
        string? userObjective,
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(userObjective) || string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (finalAnswer.Trim().Length < 8)
            return false;

        var minOverlap = AgenticGuardrailConfigReader.GetDouble(configJson, "minOverlap", 0.08);
        var objTokens = Tokenize(userObjective);
        if (objTokens.Count < 2)
            return false;

        var ansTokens = Tokenize(finalAnswer);
        if (ansTokens.Count == 0)
            return true;

        var overlap = objTokens.Count(t => ansTokens.Contains(t));
        var ratio = overlap / (double)objTokens.Count;
        if (ratio >= minOverlap)
            return false;

        // Strong IDs from objective must appear regardless of overlap ratio.
        foreach (Match m in IssueKeyRegex().Matches(userObjective))
        {
            if (!finalAnswer.Contains(m.Value, StringComparison.OrdinalIgnoreCase))
            {
                feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                    ?? TenantLocale.Select(
                        runtimeConfig.DefaultLanguage,
                        "Rejected: answer is not relevant to the user objective.",
                        "Rejeitado: a resposta não é relevante para o objetivo.");
                return true;
            }
        }

        if (ratio < minOverlap)
        {
            feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                ?? TenantLocale.Select(
                    runtimeConfig.DefaultLanguage,
                    "Rejected: answer is not relevant to the user objective.",
                    "Rejeitado: a resposta não é relevante para o objetivo.");
            return true;
        }

        return false;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            var w = m.Value;
            if (w.Length < 3 || Stopwords.Contains(w))
                continue;
            set.Add(w);
        }

        return set;
    }

    [GeneratedRegex(@"[a-z0-9áéíóúãõâêôç-]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();
}
