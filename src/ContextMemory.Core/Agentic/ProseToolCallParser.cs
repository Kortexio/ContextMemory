using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Promotes tool invocations written as assistant prose/JSON/XML into structured <see cref="OllamaToolCall"/>s.
/// Small local models often narrate calls instead of emitting native <c>tool_calls</c>.
/// </summary>
public static partial class ProseToolCallParser
{
    public static IReadOnlyList<OllamaToolCall>? TryParse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.Trim();

        var xml = TryParseXmlStyle(trimmed);
        if (xml is { Count: > 0 })
            return xml;

        var invoke = TryParseInvokeStyle(trimmed);
        if (invoke is { Count: > 0 })
            return invoke;

        var callWith = TryParseCallWithStyle(trimmed);
        if (callWith is { Count: > 0 })
            return callWith;

        foreach (var candidate in EnumerateCandidates(trimmed))
        {
            var parsed = TryParseJsonPayload(candidate);
            if (parsed is { Count: > 0 })
                return parsed;
        }

        return null;
    }

    private static IReadOnlyList<OllamaToolCall>? TryParseXmlStyle(string content)
    {
        var list = new List<OllamaToolCall>();
        foreach (Match match in ToolCallXmlRegex().Matches(content))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || !LooksLikeToolName(name))
                continue;

            var argsBody = match.Groups["body"].Value;
            var args = ParseXmlArguments(argsBody);
            list.Add(new OllamaToolCall(new OllamaFunctionCall(name, args)));
        }

        // <function=name>...</function> (Qwen-style)
        foreach (Match match in FunctionXmlRegex().Matches(content))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || !LooksLikeToolName(name))
                continue;

            var argsBody = match.Groups["body"].Value;
            var args = ParseXmlArguments(argsBody);
            list.Add(new OllamaToolCall(new OllamaFunctionCall(name, args)));
        }

        return list.Count > 0 ? list : null;
    }

    private static string ParseXmlArguments(string body)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (Match param in ParameterXmlRegex().Matches(body))
        {
            var key = param.Groups["key"].Value.Trim();
            var value = param.Groups["value"].Value.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;
            dict[key] = TryCoerceJsonValue(value);
        }

        if (dict.Count == 0)
        {
            var trimmed = body.Trim();
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
                return trimmed;
            return "{}";
        }

        return JsonSerializer.Serialize(dict);
    }

    private static object? TryCoerceJsonValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static IReadOnlyList<OllamaToolCall>? TryParseInvokeStyle(string content)
    {
        var list = new List<OllamaToolCall>();
        foreach (Match match in InvokeRegex().Matches(content))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || !LooksLikeToolName(name))
                continue;

            var argsRaw = match.Groups["args"].Success ? match.Groups["args"].Value.Trim() : "{}";
            if (!argsRaw.StartsWith('{'))
                argsRaw = "{" + argsRaw + "}";
            if (!IsValidJsonObject(argsRaw))
                argsRaw = "{}";

            list.Add(new OllamaToolCall(new OllamaFunctionCall(name, argsRaw)));
        }

        return list.Count > 0 ? list : null;
    }

    private static IReadOnlyList<OllamaToolCall>? TryParseCallWithStyle(string content)
    {
        var list = new List<OllamaToolCall>();
        foreach (Match match in CallWithRegex().Matches(content))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || !LooksLikeToolName(name))
                continue;

            var argsRaw = match.Groups["args"].Success ? match.Groups["args"].Value.Trim() : "{}";
            if (!argsRaw.StartsWith('{'))
                argsRaw = "{" + argsRaw + "}";
            if (!IsValidJsonObject(argsRaw))
                argsRaw = "{}";

            list.Add(new OllamaToolCall(new OllamaFunctionCall(name, argsRaw)));
        }

        return list.Count > 0 ? list : null;
    }

    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
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
            || toolName.Contains("describe", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("query", StringComparison.OrdinalIgnoreCase);
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

    [GeneratedRegex(
        """<tool_call>\s*<function=(?<name>[^>\s]+)\s*>(?<body>[\s\S]*?)</function>\s*</tool_call>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolCallXmlRegex();

    [GeneratedRegex(
        """<function=(?<name>[^>\s]+)\s*>(?<body>[\s\S]*?)</function>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionXmlRegex();

    [GeneratedRegex(
        """<(?:parameter|arg)\s+name=["'](?<key>[^"']+)["']\s*>(?<value>[\s\S]*?)</(?:parameter|arg)>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterXmlRegex();

    [GeneratedRegex(
        """(?i)\binvoke\s+(?<name>[a-z0-9_.\-]+(?:__[a-z0-9_.\-]+)?)\s*(?:\((?<args>\{[\s\S]*?\})\))?""",
        RegexOptions.CultureInvariant)]
    private static partial Regex InvokeRegex();

    [GeneratedRegex(
        """(?i)\bcall\s+(?<name>[a-z0-9_.\-]+(?:__[a-z0-9_.\-]+)?)\s+with\s+(?<args>\{[\s\S]*?\})""",
        RegexOptions.CultureInvariant)]
    private static partial Regex CallWithRegex();
}
