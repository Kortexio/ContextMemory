using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Policies;

/// <summary>Shared helpers for tool-call policies (signatures, wiki query extraction, locale feedback).</summary>
internal static class ToolCallPolicyShared
{
    private static readonly Regex UnicodeEscape = new(
        @"\\u([0-9a-fA-F]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly HashSet<string> QueryFocusedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "wiki_search",
        "wiki_grep"
    };

    internal static string NormalizeToolName(string toolName) =>
        toolName.Trim().ToLowerInvariant();

    internal static bool IsQueryFocused(string toolName) =>
        QueryFocusedTools.Contains(NormalizeToolName(toolName));

    internal static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);

    internal static string BuildSignature(string toolName, string? argumentsJson)
    {
        var name = NormalizeToolName(toolName);
        if (QueryFocusedTools.Contains(name))
        {
            var query = ExtractQuery(argumentsJson);
            return name + "|q=" + NormalizeText(query);
        }

        return name + "|a=" + NormalizeText(argumentsJson ?? string.Empty);
    }

    internal static string ExtractQuery(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return string.Empty;

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

            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
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

    internal static string Select(AppRuntimeConfig config, string en, string pt) =>
        TenantLocale.Select(config.DefaultLanguage, en, pt);
}
