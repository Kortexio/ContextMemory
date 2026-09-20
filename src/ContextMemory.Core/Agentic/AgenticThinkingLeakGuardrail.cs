using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects user-facing answers that are chain-of-thought / meta-reasoning instead of the result.
/// Common failure: Qwen dumps "The user wants me to…" after a guardrail rejection.
/// </summary>
public static class AgenticThinkingLeakGuardrail
{
    private static readonly string[] LeakMarkers =
    [
        "here's a thinking process",
        "here is a thinking process",
        "thinking process:",
        "the user wants me to",
        "the user is correcting me",
        "the user's prompt is",
        "looking at the session history",
        "looking at the context",
        "i need to figure out",
        "i haven't actually generated",
        "this looks like a feedback loop",
        "this implies i should",
        "wait, looking at",
        "analyze user input",
        "**analyze user input**",
        "1.  **analyze user input:**",
        "rewrite the final answer to remove internal",
        "o utilizador quer que eu",
        "preciso de perceber",
        "isto parece um feedback loop"
    ];

    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!LooksLikeThinkingLeak(finalAnswer))
            return false;

        feedback = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Rejected: do not expose chain-of-thought or discuss the harness/rejection. "
            + "Write the final answer for the end user in their language — facts only, no meta commentary.",
            "Rejeitado: não exposes chain-of-thought nem discutas o harness/rejeição. "
            + "Escreve a resposta final para o utilizador na língua dele — só factos, sem meta-comentário.");
        return true;
    }

    public static bool LooksLikeThinkingLeak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var head = text.Length > 800 ? text[..800] : text;
        var lower = head.ToLowerInvariant();

        foreach (var marker in LeakMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
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
