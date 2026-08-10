using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Promotes tool invocations written as assistant prose/JSON into structured <see cref="OllamaToolCall"/>s.
/// Small local models often narrate calls instead of emitting native <c>tool_calls</c>.
/// </summary>
public static partial class ProseToolCallParser
{
    public static IReadOnlyList<OllamaToolCall>? TryParse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.Trim();
        foreach (var candidate in EnumerateCandidates(trimmed))
        {
            var parsed = TryParseJsonPayload(candidate);
            if (parsed is { Count: > 0 })
                return parsed;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string trimmed)
    {
        foreach (Match fence in JsonFenceRegex().Matches(trimmed))
        {
            if (fence.Groups[1].Success)
                yield return fence.Groups[1].Value.Trim();
        }

        yield return trimmed;

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            yield return trimmed[start..(end + 1)];
    }

    private static IReadOnlyList<OllamaToolCall>? TryParseJsonPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryParseElement(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<OllamaToolCall>? TryParseElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var list = new List<OllamaToolCall>();
            foreach (var item in root.EnumerateArray())
            {
                var one = TryParseSingle(item);
                if (one is not null)
                    list.Add(one);
            }

            return list.Count > 0 ? list : null;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("tool_calls", out var toolCallsEl)
            || root.TryGetProperty("toolCalls", out toolCallsEl))
        {
            return TryParseElement(toolCallsEl);
        }

        var single = TryParseSingle(root);
        return single is null ? null : [single];
    }

    private static OllamaToolCall? TryParseSingle(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        if (el.TryGetProperty("function", out var functionEl) && functionEl.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetString(functionEl, "name", out var fnName) || string.IsNullOrWhiteSpace(fnName))
                return null;
            var fnArgs = SerializeArguments(functionEl, "arguments") ?? "{}";
            return new OllamaToolCall(new OllamaFunctionCall(fnName.Trim(), fnArgs));
        }

        if (!TryGetString(el, "tool", out var toolName))
            TryGetString(el, "name", out toolName);

        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        // Require a plausible tool identifier (qualified MCP or known-style names).
        if (!LooksLikeToolName(toolName))
            return null;

        var argsJson = SerializeArguments(el, "arguments")
            ?? SerializeArguments(el, "parameters")
            ?? SerializeArguments(el, "args")
            ?? "{}";

        return new OllamaToolCall(new OllamaFunctionCall(toolName.Trim(), argsJson));
    }

    private static bool LooksLikeToolName(string toolName)
    {
        if (toolName.Contains("__", StringComparison.Ordinal))
            return true;
        if (toolName.Contains('_', StringComparison.Ordinal))
            return true;
        return toolName.Contains("search", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("execute", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("describe", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SerializeArguments(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var argsEl))
            return null;

        return argsEl.ValueKind switch
        {
            JsonValueKind.String => argsEl.GetString() ?? "{}",
            JsonValueKind.Object or JsonValueKind.Array => argsEl.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "{}",
            _ => argsEl.GetRawText()
        };
    }

    private static bool TryGetString(JsonElement el, string name, out string? value)
    {
        value = null;
        if (!el.TryGetProperty(name, out var prop))
            return false;
        if (prop.ValueKind != JsonValueKind.String)
            return false;
        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    [GeneratedRegex("""```(?:json)?\s*([\s\S]*?)```""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFenceRegex();
}
