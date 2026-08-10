using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticSqlGuardrail
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

        if (!LooksLikeSql(finalAnswer))
            return false;

        var sql = ExtractSql(finalAnswer);
        var upper = sql.ToUpperInvariant();

        var dangerous =
            upper.Contains("DROP ", StringComparison.Ordinal)
            || upper.Contains("TRUNCATE ", StringComparison.Ordinal)
            || (upper.Contains("DELETE ", StringComparison.Ordinal) && !upper.Contains("WHERE", StringComparison.Ordinal))
            || (upper.Contains("UPDATE ", StringComparison.Ordinal) && !upper.Contains("WHERE", StringComparison.Ordinal));

        var unbalanced = CountChar(sql, '\'') % 2 != 0;

        if (!dangerous && !unbalanced)
            return false;

        feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
            ?? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "Rejected: SQL in the answer looks unsafe or malformed.",
                "Rejeitado: SQL na resposta parece inseguro ou malformado.");
        return true;
    }

    private static bool LooksLikeSql(string text)
    {
        var upper = text.ToUpperInvariant();
        return upper.Contains("SELECT ", StringComparison.Ordinal)
               || upper.Contains("INSERT ", StringComparison.Ordinal)
               || upper.Contains("UPDATE ", StringComparison.Ordinal)
               || upper.Contains("DELETE ", StringComparison.Ordinal)
               || upper.Contains("DROP ", StringComparison.Ordinal)
               || SqlFenceRegex().IsMatch(text);
    }

    private static string ExtractSql(string text)
    {
        var fence = SqlFenceRegex().Match(text);
        if (fence.Success)
            return fence.Groups[1].Value;
        return text;
    }

    private static int CountChar(string s, char c)
    {
        var n = 0;
        foreach (var ch in s)
        {
            if (ch == c)
                n++;
        }

        return n;
    }

    [GeneratedRegex(@"```(?:sql)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlFenceRegex();
}
