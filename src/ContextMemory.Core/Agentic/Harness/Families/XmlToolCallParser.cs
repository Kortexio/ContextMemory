using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Lift Qwen/Hermes-style XML tool markup into structured tool calls.
/// Static helper used by <see cref="QwenFamilyParser"/> — not an <see cref="IGrammarParser"/>.
/// </summary>
public static partial class XmlToolCallParser
{
    public static bool LooksLikeXmlToolMarkup(string text) =>
        ToolCallOpenRegex().IsMatch(text)
        || FunctionEqRegex().IsMatch(text)
        || ParameterEqRegex().IsMatch(text);

    public static IReadOnlyList<LiftedToolCall> ParseXmlToolCalls(string text)
    {
        if (string.IsNullOrEmpty(text) || !LooksLikeXmlToolMarkup(text))
            return Array.Empty<LiftedToolCall>();

        var source = StripThinkMarkup(text);
        var blocks = new List<string>();

        foreach (Match m in ClosedToolCallRegex().Matches(source))
            blocks.Add(m.Groups[1].Value);

        var trailing = TrailingOpenToolCallRegex().Match(source);
        if (trailing.Success)
        {
            var body = trailing.Groups[1].Value;
            if (body.Length > 0 && !blocks.Contains(body))
                blocks.Add(body);
        }
        else if (blocks.Count == 0)
        {
            var open = OpenToolCallTailRegex().Match(source);
            if (open.Success)
                blocks.Add(open.Groups[1].Value);
        }

        if (blocks.Count == 0 && FunctionEqRegex().IsMatch(source))
            blocks.Add(source);

        var outCalls = new List<LiftedToolCall>();
        foreach (var body in blocks)
        {
            var asJson = TryJsonToolCall(body, outCalls.Count);
            if (asJson is not null)
            {
                outCalls.Add(asJson);
                continue;
            }

            outCalls.AddRange(ParseFunctionBlocks(body, outCalls.Count));
        }

        return FamilySafety.DedupeIdentical(
            outCalls.Where(c => !string.IsNullOrEmpty(c.Name)).ToList());
    }

    private static string StripThinkMarkup(string text) =>
        ThinkBlockRegex().Replace(text, "")
            .Trim();

    private static LiftedToolCall? TryJsonToolCall(string body, int index)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{'))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("function", out var fn)
                && fn.ValueKind == JsonValueKind.Object
                && fn.TryGetProperty("name", out var fnName)
                && fnName.ValueKind == JsonValueKind.String)
            {
                var name = fnName.GetString()!;
                object? argsObj = new Dictionary<string, object?>();
                if (fn.TryGetProperty("arguments", out var argsEl))
                {
                    if (argsEl.ValueKind == JsonValueKind.String)
                    {
                        try { argsObj = JsonSerializer.Deserialize<object>(argsEl.GetString() ?? "{}"); }
                        catch { argsObj = new Dictionary<string, object?>(); }
                    }
                    else
                    {
                        argsObj = JsonSerializer.Deserialize<object>(argsEl.GetRawText());
                    }
                }

                return new LiftedToolCall($"xml_{index}", name, JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>()));
            }

            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString()!;
                JsonElement argsEl = default;
                var hasArgs = root.TryGetProperty("arguments", out argsEl)
                    || root.TryGetProperty("args", out argsEl);
                object? argsObj = hasArgs && argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                    : new Dictionary<string, object?>();
                return new LiftedToolCall($"xml_{index}", name, JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>()));
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static List<LiftedToolCall> ParseFunctionBlocks(string body, int startIndex)
    {
        var outCalls = new List<LiftedToolCall>();
        var i = startIndex;
        foreach (Match m in FunctionBlockRegex().Matches(body))
        {
            var name = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            outCalls.Add(new LiftedToolCall($"xml_{i++}", name, JsonSerializer.Serialize(ParseParameterArgs(inner))));
        }

        return outCalls;
    }

    private static Dictionary<string, object?> ParseParameterArgs(string inner)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (Match pm in ParameterEqBlockRegex().Matches(inner))
            args[pm.Groups[1].Value] = FamilySafety.CoerceScalar(pm.Groups[2].Value);

        foreach (Match pm in ParameterNamedBlockRegex().Matches(inner))
            args[pm.Groups[1].Value] = FamilySafety.CoerceScalar(pm.Groups[2].Value);

        return args;
    }

    [GeneratedRegex(@"<tool_call\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolCallOpenRegex();

    [GeneratedRegex(@"<function\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionEqRegex();

    [GeneratedRegex(@"<parameter\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterEqRegex();

    [GeneratedRegex(@"<think\b[^>]*>[\s\S]*?</think>|<think\b[^>]*>[\s\S]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex(@"<tool_call\b[^>]*>([\s\S]*?)</tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosedToolCallRegex();

    [GeneratedRegex(@"(?:</tool_call>\s*)?<tool_call\b[^>]*>(?![\s\S]*</tool_call>)([\s\S]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingOpenToolCallRegex();

    [GeneratedRegex(@"<tool_call\b[^>]*>([\s\S]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenToolCallTailRegex();

    [GeneratedRegex(@"<function\s*=\s*([A-Za-z0-9_]+)\s*>([\s\S]*?)(?:</function>|(?=<function\s*=)|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionBlockRegex();

    [GeneratedRegex(@"<parameter\s*=\s*([A-Za-z0-9_]+)\s*>([\s\S]*?)(?:</parameter>|</[A-Za-z0-9_]+>|(?=<parameter\s*=)|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterEqBlockRegex();

    [GeneratedRegex(@"<parameter\s+name\s*=\s*[""']([A-Za-z0-9_]+)[""']\s*>([\s\S]*?)(?:</parameter>|</[A-Za-z0-9_]+>)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamedBlockRegex();
}
