using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticPromptAddressGuardrail
{
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

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in IssueKeyRegex().Matches(userObjective))
            required.Add(m.Value);

        foreach (Match m in NumberedItemRegex().Matches(userObjective))
        {
            var item = m.Groups["item"].Value.Trim();
            if (item.Length >= 3)
                required.Add(item);
        }

        if (required.Count == 0)
            return false;

        var missing = required
            .Where(r => !finalAnswer.Contains(r, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
            return false;

        feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
            ?? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                $"Rejected: answer does not address: {string.Join(", ", missing)}.",
                $"Rejeitado: a resposta não cobre: {string.Join(", ", missing)}.");
        return true;
    }

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();

    [GeneratedRegex(@"(?:^|\n)\s*(?:\d+[\).\]]|-)\s*(?<item>[^\n]{3,80})", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedItemRegex();
}
