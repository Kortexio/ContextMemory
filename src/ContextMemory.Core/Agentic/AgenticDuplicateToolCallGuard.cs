using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Blocks identical successful tool calls (same name + normalized args) so weak models
/// cannot spin on the same wiki_search / query forever. Returns an actionable observation.
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

    public static bool TryReject(
        string toolName,
        string? argumentsJson,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName) || steps.Count == 0)
            return false;

        var signature = BuildSignature(toolName, argumentsJson);
        if (string.IsNullOrEmpty(signature))
            return false;

        var alreadySucceeded = steps.Any(s =>
            s.Success
            && string.Equals(NormalizeToolName(s.ToolName), NormalizeToolName(toolName), StringComparison.Ordinal)
            && string.Equals(BuildSignature(s.ToolName, s.Arguments), signature, StringComparison.Ordinal));

        if (!alreadySucceeded)
            return false;

        feedback = BuildFeedback(toolName, runtimeConfig);
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
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("query", out var q)
                && q.ValueKind == JsonValueKind.String)
            {
                return q.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Fall through to raw normalize.
        }

        return argumentsJson;
    }

    private static string BuildFeedback(string toolName, AppRuntimeConfig runtimeConfig)
    {
        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var isWiki = QueryFocusedTools.Contains(NormalizeToolName(toolName));

        if (hasMcp && isWiki)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: identical wiki_search already succeeded — do NOT repeat the same query. "
                + "Either change the query substantially OR call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects; tool_describe if the schema is unclear).",
                "Rejeitado: wiki_search idêntica já teve sucesso — NÃO repitas a mesma query. "
                + "Ou muda a query de forma substancial OU chama agora uma tool MCP configurada como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects; tool_describe se o schema for unclear).");
        }

        if (hasMcp)
        {
            return TenantLocale.Select(
                lang,
                $"Rejected: identical `{toolName}` already succeeded — do NOT repeat the same arguments. "
                + "Try different arguments or another MCP/catalog tool as ONLY JSON "
                + "{\"tool\":\"name\",\"arguments\":{...}}.",
                $"Rejeitado: `{toolName}` idêntica já teve sucesso — NÃO repitas os mesmos arguments. "
                + "Tenta arguments diferentes ou outra tool MCP/catálogo como APENAS JSON "
                + "{\"tool\":\"nome\",\"arguments\":{...}}.");
        }

        if (isWiki)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: identical wiki_search already succeeded — do NOT repeat. "
                + "Change the query substantially or answer from the evidence you already have.",
                "Rejeitado: wiki_search idêntica já teve sucesso — NÃO repitas. "
                + "Muda a query de forma substancial ou responde com a evidência que já tens.");
        }

        return TenantLocale.Select(
            lang,
            $"Rejected: identical `{toolName}` already succeeded — do NOT repeat the same arguments. "
            + "Change arguments or answer from existing evidence.",
            $"Rejeitado: `{toolName}` idêntica já teve sucesso — NÃO repitas os mesmos arguments. "
            + "Muda os arguments ou responde com a evidência existente.");
    }

    private static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);
}
