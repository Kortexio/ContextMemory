using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Microsoft Phi-4-mini: literal <c>functools[{…}, …]</c> prefix + JSON array.
/// </summary>
public sealed partial class Phi4FamilyParser : IGrammarParser
{
    public string Id => "phi4";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match m in StartRegex().Matches(masked))
        {
            var openIndex = m.Index + m.Length - 1;
            var obj = FamilySafety.ExtractBalanced(masked, openIndex, '[', ']');
            if (obj is null)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(obj.Value.Json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;
                var any = false;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                        continue;
                    var name = nameEl.GetString()!;
                    JsonElement argsEl = default;
                    var hasArgs = item.TryGetProperty("arguments", out argsEl)
                        || item.TryGetProperty("args", out argsEl);
                    object? argsObj = hasArgs && argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                        : new Dictionary<string, object?>();
                    calls.Add(new LiftedToolCall(
                        $"phi4_{calls.Count}",
                        name,
                        JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>())));
                    any = true;
                }

                if (any)
                    spans.Add((m.Index, obj.Value.EndIndex));
            }
            catch
            {
                /* malformed — leave as text */
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"\bfunctools\s*\[", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"\bfunctools\s*(\[)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartRegex();
}
