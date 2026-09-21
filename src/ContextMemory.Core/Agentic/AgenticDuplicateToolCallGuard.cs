using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Blocks empty / identical wiki_search (and similar) loops so weak models cannot spin
/// on <c>{}</c> or the same query forever — whether the prior call succeeded or failed.
/// Non-wiki tools still only block after an identical <em>successful</em> call.
/// </summary>
public static class AgenticDuplicateToolCallGuard
{
    private static readonly Regex UnicodeEscape = new(
        @"\\u([0-9a-fA-F]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> QueryFocusedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "wiki_search",
        "wiki_grep"
    };

    /// <summary>Max wiki_search/wiki_grep attempts (success or fail) before forcing a pivot.</summary>
    public const int MaxWikiAttemptsPerTurn = 2;

    public static bool TryReject(
        string toolName,
        string? argumentsJson,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        var name = NormalizeToolName(toolName);
        if (QueryFocusedTools.Contains(name))
        {
            var wikiAttempts = steps.Count(s =>
                !s.RejectedByGuard
                && QueryFocusedTools.Contains(NormalizeToolName(s.ToolName)));
            if (wikiAttempts >= MaxWikiAttemptsPerTurn)
            {
                feedback = BuildWikiBudgetFeedback(runtimeConfig);
                return true;
            }

            var query = ExtractQuery(argumentsJson);
            if (string.IsNullOrWhiteSpace(query))
            {
                feedback = BuildEmptyQueryFeedback(name, runtimeConfig);
                return true;
            }
        }

        if (steps.Count == 0)
            return false;

        var signature = BuildSignature(toolName, argumentsJson);
        if (string.IsNullOrEmpty(signature))
            return false;

        var sameSignature = steps.Where(s =>
            string.Equals(NormalizeToolName(s.ToolName), name, StringComparison.Ordinal)
            && string.Equals(BuildSignature(s.ToolName, s.Arguments), signature, StringComparison.Ordinal));

        if (QueryFocusedTools.Contains(name))
        {
            // Empty/identical wiki retries never help — block after any prior attempt.
            if (!sameSignature.Any())
                return false;

            feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: sameSignature.All(s => !s.Success));
            return true;
        }

        var alreadySucceeded = sameSignature.Any(s => s.Success);
        if (!alreadySucceeded)
            return false;

        feedback = BuildFeedback(toolName, runtimeConfig, afterFailure: false);
        return true;
    }

    public static string BuildSignature(string toolName, string? argumentsJson)
    {
        var name = NormalizeToolName(toolName);
        if (QueryFocusedTools.Contains(name))
        {
            var query = ExtractQuery(argumentsJson);
            return name + "|q=" + NormalizeText(query);
        }

        return name + "|a=" + NormalizeText(argumentsJson ?? string.Empty);
    }

    internal static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var unescaped = UnicodeEscape.Replace(value, m =>
        {
            var code = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return char.ConvertFromUtf32(code);
        });

        var sb = new StringBuilder(unescaped.Length);
        var prevSpace = false;
        foreach (var ch in unescaped.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                    sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeToolName(string toolName) =>
        toolName.Trim().ToLowerInvariant();

    private static string ExtractQuery(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return string.Empty;

            // wiki_search uses "query"; wiki_grep uses "pattern" (case-insensitive).
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    continue;

                if (string.Equals(prop.Name, "query", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(prop.Name, "pattern", StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.GetString() ?? string.Empty;
                }
            }

            // {} or object without query/pattern → empty (do not fall back to raw JSON).
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string BuildWikiBudgetFeedback(AppRuntimeConfig runtimeConfig)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
                + "Do NOT call wiki again. Call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
                + "NÃO chames wiki outra vez. Chama agora uma tool MCP como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).");
        }

        return TenantLocale.Select(
            lang,
            "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
            + "Answer from evidence already gathered or change approach — do not call wiki again.",
            "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
            + "Responde com a evidência já recolhida ou muda de abordagem — não chames wiki outra vez.");
    }

    private static string BuildEmptyQueryFeedback(string toolName, AppRuntimeConfig runtimeConfig)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var field = string.Equals(toolName, "wiki_grep", StringComparison.Ordinal) ? "pattern" : "query";

        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: `{toolName}` needs a non-empty \"{field}\". "
                + $"Retry as ONLY JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}} "
                + "OR call a configured MCP tool now "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                $"Rejeitado: `{toolName}` precisa de \"{field}\" não vazio. "
                + $"Repete como APENAS JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}} "
                + "OU chama agora uma tool MCP "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).");
        }

        return TenantLocale.Select(
            lang,
            $"Rejected: `{toolName}` needs a non-empty \"{field}\". "
            + $"Retry as ONLY JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}}.",
            $"Rejeitado: `{toolName}` precisa de \"{field}\" não vazio. "
            + $"Repete como APENAS JSON {{\"tool\":\"{toolName}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}}.");
    }

    private static string BuildFeedback(string toolName, AppRuntimeConfig runtimeConfig, bool afterFailure)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var isWiki = QueryFocusedTools.Contains(NormalizeToolName(toolName));
        var prior = afterFailure
            ? TenantLocale.Select(lang, "already failed", "já falhou")
            : TenantLocale.Select(lang, "already succeeded", "já teve sucesso");

        if (hasMcp && isWiki)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical wiki_search {prior} — do NOT repeat the same query. "
                + "Either change the query substantially OR call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects; tool_describe if the schema is unclear).",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas a mesma query. "
                + "Ou muda a query de forma substancial OU chama agora uma tool MCP configurada como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects; tool_describe se o schema for unclear).");
        }

        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
                + "Try different arguments or another MCP/catalog tool as ONLY JSON "
                + "{\"tool\":\"name\",\"arguments\":{...}}.",
                $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
                + "Tenta arguments diferentes ou outra tool MCP/catálogo como APENAS JSON "
                + "{\"tool\":\"nome\",\"arguments\":{...}}.");
        }

        if (isWiki)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical wiki_search {prior} — do NOT repeat. "
                + "Change the query substantially or answer from the evidence you already have.",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas. "
                + "Muda a query de forma substancial ou responde com a evidência que já tens.");
        }

        return TenantLocale.Select(
            lang,
            $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
            + "Change arguments or answer from existing evidence.",
            $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
            + "Muda os arguments ou responde com a evidência existente.");
    }

    private static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);
}
