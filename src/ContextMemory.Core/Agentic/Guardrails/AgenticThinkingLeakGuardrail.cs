using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects user-facing answers that are chain-of-thought / meta-reasoning instead of the result.
/// Patterns and feedback come only from Admin <c>ConfigJson</c>. Empty patterns ⇒ no-op.
/// </summary>
public static class AgenticThinkingLeakGuardrail
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

        var patterns = AgenticGuardrailConfigReader.GetStringList(configJson, "patterns");
        if (patterns.Count == 0)
            return false;

        if (!LooksLikeThinkingLeak(finalAnswer, patterns))
            return false;

        feedback = AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage);
        return true;
    }

    public static bool LooksLikeThinkingLeak(string text, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(text) || patterns.Count == 0)
            return false;

        var head = text.Length > 800 ? text[..800] : text;
        var lower = head.ToLowerInvariant();

        foreach (var marker in patterns)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;
            if (lower.Contains(marker.ToLowerInvariant(), StringComparison.Ordinal))
                return true;
        }

        // Numbered "thinking outline" that never states a user-facing claim in the first lines.
        if (lower.StartsWith("1.", StringComparison.Ordinal)
            && (lower.Contains("analyze", StringComparison.Ordinal)
                || lower.Contains("context/tools", StringComparison.Ordinal)
                || lower.Contains("goal:", StringComparison.Ordinal)))
            return true;

        return false;
    }
}
