using System.Globalization;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects final answers that cite specific numeric values (prices, percentages, dates, elevated counts)
/// without those values appearing in successful non-discovery tool outputs.
/// Tunable via <c>configJson.minSpecificsToReject</c> (default 1).
/// </summary>
public static partial class AgenticNumericsGroundingGuardrail
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

        var minToReject = Math.Max(1, AgenticGuardrailConfigReader.GetInt(configJson, "minSpecificsToReject", 1));
        var specifics = ExtractSpecifics(finalAnswer);
        if (specifics.Count == 0)
            return false;

        var evidence = BuildEvidenceBlob(steps);
        var ungrounded = new List<string>();

        foreach (var value in specifics)
        {
            if (IsGrounded(value, evidence))
                continue;
            ungrounded.Add(value);
        }

        if (ungrounded.Count < minToReject)
            return false;

        var sample = string.Join(", ", ungrounded.Take(3).Select(v => $"'{v}'"));
        feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
            ?? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                $"Rejected: numeric values without tool evidence ({sample}). "
                + "Emit tool_calls (wiki_search / MCP / web_search) or remove unsupported numbers.",
                $"Rejeitado: valores numéricos sem evidência de tools ({sample}). "
                + "Emite tool_calls (wiki_search / MCP / web_search) ou remove números sem suporte.");
        return true;
    }

    /// <summary>Public for unit tests — extracts candidate numeric tokens from text.</summary>
    public static IReadOnlyList<string> ExtractSpecifics(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in MonetaryRegex().Matches(text))
            set.Add(NormalizeToken(m.Value));

        foreach (Match m in PercentRegex().Matches(text))
            set.Add(NormalizeToken(m.Value));

        foreach (Match m in IsoDateRegex().Matches(text))
            set.Add(m.Value);

        foreach (Match m in SlashDateRegex().Matches(text))
            set.Add(m.Value);

        foreach (Match m in ElevatedCountRegex().Matches(text))
            set.Add(NormalizeToken(m.Groups["num"].Value));

        set.RemoveWhere(string.IsNullOrWhiteSpace);
        return set.ToList();
    }

    private static bool IsGrounded(string value, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return false;

        if (evidence.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Compare digit/separator cores so "€1.234,56" matches "1234,56" or "1.234,56" in evidence.
        var core = DigitsAndSeparators(value);
        if (core.Length >= 2 && evidence.Contains(core, StringComparison.OrdinalIgnoreCase))
            return true;

        // Also try a separator-normalized form (strip thousand separators).
        var compact = CompactDigits(value);
        if (compact.Length >= 2)
        {
            if (evidence.Contains(compact, StringComparison.OrdinalIgnoreCase))
                return true;

            // Evidence may keep local separators; search for compact digits loosely.
            var evidenceCompact = CompactDigits(evidence);
            if (evidenceCompact.Contains(compact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildEvidenceBlob(IReadOnlyList<AgentExecutionStep> steps)
    {
        var parts = steps
            .Where(s => s.Success && !SessionDiscoveryTools.IsDiscoveryTool(s.ToolName))
            .Select(s => s.Output ?? string.Empty);
        return string.Join('\n', parts);
    }

    private static string NormalizeToken(string raw) =>
        raw.Trim();

    private static string DigitsAndSeparators(string raw) =>
        new(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-' or '/').ToArray());

    private static string CompactDigits(string raw)
    {
        // Keep digits and a single decimal marker if present; drop thousand separators heuristically.
        var digits = DigitsAndSeparators(raw);
        if (digits.Length == 0)
            return string.Empty;

        // ISO dates / slash dates: keep as-is without stripping.
        if (digits.Contains('-', StringComparison.Ordinal) || digits.Contains('/', StringComparison.Ordinal))
            return digits;

        // Prefer last separator as decimal when both . and , appear, or when trailing fraction is 1–2 digits.
        var lastSep = Math.Max(digits.LastIndexOf('.'), digits.LastIndexOf(','));
        if (lastSep < 0)
            return new string(digits.Where(char.IsDigit).ToArray());

        var frac = digits[(lastSep + 1)..];
        if (frac.Length is 1 or 2 && frac.All(char.IsDigit))
        {
            var intPart = new string(digits[..lastSep].Where(char.IsDigit).ToArray());
            return intPart + CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator + frac;
        }

        return new string(digits.Where(char.IsDigit).ToArray());
    }

    [GeneratedRegex(
        @"(?:€|\$|USD|EUR|GBP|BRL|R\$)\s?\d[\d.,]*|\d[\d.,]*\s?(?:€|\$|USD|EUR|GBP|BRL)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonetaryRegex();

    [GeneratedRegex(
        @"\b\d{1,3}(?:[.,]\d+)?\s?%",
        RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(
        @"\b(?:19|20)\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(
        @"\b(?:0?[1-9]|[12]\d|3[01])[/-](?:0?[1-9]|1[0-2])[/-](?:19|20)\d{2}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex SlashDateRegex();

    /// <summary>
    /// Elevated counts with thousand separators or 4+ digit integers followed by a quantity noun.
    /// Avoids matching small bare integers (years alone, iteration counts, etc.).
    /// </summary>
    [GeneratedRegex(
        @"\b(?<num>\d{1,3}(?:[.,]\d{3})+|\d{4,})\s*(?:registros?|registos?|records?|items?|itens?|users?|utilizadores?|clientes?|customers?|invoices?|faturas?|tickets?|linhas?|rows?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ElevatedCountRegex();
}
