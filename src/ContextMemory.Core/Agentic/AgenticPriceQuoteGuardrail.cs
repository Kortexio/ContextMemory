using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticPriceQuoteGuardrail
{
    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var prices = PriceRegex().Matches(finalAnswer);
        if (prices.Count == 0)
            return false;

        var evidence = string.Join('\n', steps
            .Where(s => s.Success && !SessionDiscoveryTools.IsDiscoveryTool(s.ToolName))
            .Select(s => s.Output ?? string.Empty));

        foreach (Match m in prices)
        {
            var token = NormalizePrice(m.Value);
            if (string.IsNullOrEmpty(token))
                continue;

            if (string.IsNullOrWhiteSpace(evidence)
                || !evidence.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                // Also try raw digit group from the match
                var digits = new string(m.Value.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
                if (!string.IsNullOrWhiteSpace(evidence)
                    && !string.IsNullOrWhiteSpace(digits)
                    && evidence.Contains(digits, StringComparison.Ordinal))
                {
                    continue;
                }

                feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                    ?? TenantLocale.Select(
                        runtimeConfig.DefaultLanguage,
                        "Rejected: price quote without tool evidence.",
                        "Rejeitado: preço sem evidência de tools.");
                return true;
            }
        }

        return false;
    }

    private static string NormalizePrice(string raw)
    {
        var digits = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        return digits;
    }

    [GeneratedRegex(
        @"(?:€|\$|USD|EUR|GBP)\s?\d[\d.,]*|\d[\d.,]*\s?(?:€|USD|EUR|GBP)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PriceRegex();
}
