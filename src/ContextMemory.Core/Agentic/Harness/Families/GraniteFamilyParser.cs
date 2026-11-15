using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// IBM Granite: Hermes-style JSON inside <c>&lt;tool_call&gt;</c>, or a bare pythonic call
/// when the whole trimmed content is exactly one such call.
/// </summary>
public sealed partial class GraniteFamilyParser : IGrammarParser
{
    public string Id => "granite";

    public bool Detect(string text) =>
        DetectRegex().IsMatch(text.Trim()) || ToolCallOpenRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match bm in BlockRegex().Matches(masked))
        {
            var call = CallFromJsonBlock(bm.Groups[1].Value, calls.Count);
            if (call is not null)
            {
                calls.Add(call);
                spans.Add((bm.Index, bm.Index + bm.Length));
            }
        }

        if (calls.Count == 0)
        {
            var bare = BarePythonicRegex().Match(masked.Trim());
            if (bare.Success)
            {
                var args = PythonicArgs.Parse(bare.Groups[2].Value);
                if (args is not null)
                {
                    calls.Add(new LiftedToolCall("granite_0", bare.Groups[1].Value, PythonicArgs.ToJson(args)));
                    var idx = masked.IndexOf(bare.Value, StringComparison.Ordinal);
                    if (idx >= 0)
                        spans.Add((idx, idx + bare.Length));
                }
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    private static LiftedToolCall? CallFromJsonBlock(string body, int index)
    {
        try
        {
            using var doc = JsonDocument.Parse(body.Trim());
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                return null;
            var name = nameEl.GetString()!;

            JsonElement argsEl = default;
            var hasArgs = root.TryGetProperty("arguments", out argsEl)
                || root.TryGetProperty("args", out argsEl);
            if (!hasArgs)
                return new LiftedToolCall($"granite_{index}", name, "{}");

            if (argsEl.ValueKind == JsonValueKind.String)
            {
                try
                {
                    var nested = JsonSerializer.Deserialize<object>(argsEl.GetString() ?? "{}");
                    return new LiftedToolCall(
                        $"granite_{index}",
                        name,
                        JsonSerializer.Serialize(nested ?? new Dictionary<string, object?>()));
                }
                catch
                {
                    return null;
                }
            }

            if (argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                return new LiftedToolCall(
                    $"granite_{index}",
                    name,
                    JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                        ?? new Dictionary<string, object?>()));
            }

            return new LiftedToolCall($"granite_{index}", name, "{}");
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"<tool_call>[\s\S]*?</tool_call>|^\s*[A-Za-z_][A-Za-z0-9_.]*\([^()]*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolCallOpenRegex();

    [GeneratedRegex(@"<tool_call>([\s\S]*?)</tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_.]*)\(([^()]*)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BarePythonicRegex();
}
