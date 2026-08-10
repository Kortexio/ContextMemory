using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Shared pattern-list enforcement for inappropriate / offensive / competitor kinds.
/// </summary>
public static class AgenticPatternListGuardrail
{
    private static readonly string[] DefaultInappropriate =
    [
        "child porn", "childporn", "rape porn", "bestiality"
    ];

    private static readonly string[] DefaultOffensive =
    [
        "kys", "kill yourself", "nigger", "faggot"
    ];

    public static bool TryGetRejectionFeedback(
        string kind,
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var configured = AgenticGuardrailConfigReader.GetStringList(configJson, "patterns");
        IEnumerable<string> patterns = configured;
        if (configured.Count == 0)
        {
            if (string.Equals(kind, AgenticGuardrailKinds.InappropriateContent, StringComparison.OrdinalIgnoreCase))
                patterns = DefaultInappropriate;
            else if (string.Equals(kind, AgenticGuardrailKinds.OffensiveLanguage, StringComparison.OrdinalIgnoreCase))
                patterns = DefaultOffensive;
            else
                return false; // competitor with empty list = no-op
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            if (finalAnswer.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                    ?? TenantLocale.Select(
                        runtimeConfig.DefaultLanguage,
                        $"Rejected: blocked pattern '{pattern}'.",
                        $"Rejeitado: padrão bloqueado '{pattern}'.");
                return true;
            }
        }

        return false;
    }
}
