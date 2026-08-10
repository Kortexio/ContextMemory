using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticPiiGuardrail
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

        if (EmailRegex().IsMatch(finalAnswer)
            || IbanRegex().IsMatch(finalAnswer)
            || SsnRegex().IsMatch(finalAnswer)
            || HasLikelyCardNumber(finalAnswer)
            || NifRegex().IsMatch(finalAnswer))
        {
            feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
                ?? TenantLocale.Select(
                    runtimeConfig.DefaultLanguage,
                    "Rejected: possible PII in the answer. Redact sensitive identifiers.",
                    "Rejeitado: possível PII na resposta. Redige identificadores sensíveis.");
            return true;
        }

        return false;
    }

    private static bool HasLikelyCardNumber(string text)
    {
        foreach (Match m in CardDigitsRegex().Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits))
                return true;
        }

        return false;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alt)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }

            sum += n;
            alt = !alt;
        }

        return sum % 10 == 0;
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b\d{3}[.\s]?\d{3}[.\s]?\d{3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex NifRegex();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CardDigitsRegex();
}
