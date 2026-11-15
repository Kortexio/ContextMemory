using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Mistral sentinel tokens: <c>[TOOL_CALLS] [{…}]</c> and V11 <c>[TOOL_CALLS]name[CALL_ID]id[ARGS]{…}</c>.
/// </summary>
public sealed partial class MistralFamilyParser : IGrammarParser
{
    public string Id => "mistral";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match am in ArrayStartRegex().Matches(masked))
        {
            var openIndex = am.Index + am.Length - 1;
            var obj = FamilySafety.ExtractBalanced(masked, openIndex, '[', ']');
            if (obj is null)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(obj.Value.Json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;
                var fromArr = CallsFromArray(doc.RootElement, calls.Count);
                if (fromArr.Count > 0)
                {
                    calls.AddRange(fromArr);
                    spans.Add((am.Index, obj.Value.EndIndex));
                }
            }
            catch
            {
                /* malformed — leave as text */
            }
        }

        if (calls.Count == 0)
        {
            foreach (Match m in V11CallRegex().Matches(masked))
            {
                try
                {
                    var args = JsonSerializer.Deserialize<object>(m.Groups[3].Value);
                    calls.Add(new LiftedToolCall(
                        m.Groups[2].Value,
                        m.Groups[1].Value,
                        JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));
                    spans.Add((m.Index, m.Index + m.Length));
                }
                catch
                {
                    /* skip malformed call */
                }
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    private static List<LiftedToolCall> CallsFromArray(JsonElement arr, int startIndex)
    {
        var outCalls = new List<LiftedToolCall>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;
            var name = nameEl.GetString()!;
            var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()!
                : $"mistral_{startIndex + outCalls.Count}";

            JsonElement argsEl = default;
            var hasArgs = item.TryGetProperty("arguments", out argsEl)
                || item.TryGetProperty("args", out argsEl);
            object? argsObj = hasArgs && argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                : new Dictionary<string, object?>();

            outCalls.Add(new LiftedToolCall(
                id,
                name,
                JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>())));
        }

        return outCalls;
    }

    [GeneratedRegex(@"\[TOOL_CALLS\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"\[TOOL_CALLS\]\s*(\[)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArrayStartRegex();

    [GeneratedRegex(@"\[TOOL_CALLS\]([A-Za-z0-9_.:-]+)\[CALL_ID\]([A-Za-z0-9]+)\[ARGS\](\{[\s\S]*?\})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex V11CallRegex();
}
