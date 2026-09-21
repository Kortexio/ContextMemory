using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects tool calls that omit required schema fields (e.g. <c>shell_execute {}</c>).
/// Open stubs (MCP) are skipped — without a real schema there is nothing to validate.
/// </summary>
public static class AgenticRequiredArgumentsGuard
{
    public static bool TryReject(
        string toolName,
        string? argumentsJson,
        IReadOnlyList<OllamaTool>? turnCatalog,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName) || turnCatalog is null || turnCatalog.Count == 0)
            return false;

        var tool = turnCatalog.FirstOrDefault(t =>
            string.Equals(t.Function.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            return false;

        if (McpPinnedToolFactory.IsOpenStubParameters(tool.Function.Parameters))
            return false;

        var required = ReadRequiredFields(tool.Function.Parameters);
        if (required.Count == 0)
            return false;

        using var argsDoc = ParseArgs(argumentsJson);
        var args = argsDoc.RootElement;
        var missing = new List<string>();
        foreach (var field in required)
        {
            if (!TryGetPropertyIgnoreCase(args, field, out var value) || IsEmpty(value))
                missing.Add(field);
        }

        if (missing.Count == 0)
            return false;

        feedback = BuildFeedback(toolName, missing, runtimeConfig);
        return true;
    }

    private static List<string> ReadRequiredFields(object? parameters)
    {
        if (parameters is null)
            return [];

        try
        {
            using var doc = parameters switch
            {
                JsonElement el => JsonDocument.Parse(el.GetRawText()),
                string s => JsonDocument.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s),
                _ => JsonDocument.Parse(JsonSerializer.Serialize(parameters))
            };

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            if (!TryGetPropertyIgnoreCase(doc.RootElement, "required", out var requiredEl)
                || requiredEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<string>();
            foreach (var item in requiredEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(name);
                }
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonDocument ParseArgs(string? argumentsJson)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static bool IsEmpty(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            JsonValueKind.Object => !value.EnumerateObject().Any(),
            _ => false
        };

    private static string BuildFeedback(
        string toolName,
        IReadOnlyList<string> missing,
        AppRuntimeConfig runtimeConfig)
    {
        var fields = string.Join(", ", missing.Select(f => $"\"{f}\""));
        var exampleArgs = string.Join(
            ",",
            missing.Select(f => $"\"{f}\":\"…\""));
        var example = $"{{\"tool\":\"{toolName}\",\"arguments\":{{{exampleArgs}}}}}";

        return TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            $"Rejected: `{toolName}` is missing required field(s): {fields}. "
            + $"Retry as ONLY JSON {example}.",
            $"Rejeitado: `{toolName}` sem campo(s) obrigatório(s): {fields}. "
            + $"Repete como APENAS JSON {example}.");
    }
}
