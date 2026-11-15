using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Llama 3.x / 4 <c>&lt;|python_tag|&gt;</c> prefix (JSON object or pythonic call).
/// </summary>
public sealed partial class LlamaFamilyParser : IGrammarParser
{
    public string Id => "llama";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match jm in JsonStartRegex().Matches(masked))
        {
            var openIndex = jm.Index + jm.Length - 1;
            var obj = FamilySafety.ExtractBalanced(masked, openIndex, '{', '}');
            if (obj is null)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(obj.Value.Json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameEl.GetString()!;
                JsonElement argsEl = default;
                var hasArgs = root.TryGetProperty("parameters", out argsEl)
                    || root.TryGetProperty("arguments", out argsEl);
                object? argsObj = hasArgs
                    ? JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                    : new Dictionary<string, object?>();
                calls.Add(new LiftedToolCall(
                    $"llama_{calls.Count}",
                    name,
                    JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>())));
                spans.Add((jm.Index, obj.Value.EndIndex));
            }
            catch
            {
                /* leave as text */
            }
        }

        if (calls.Count == 0)
        {
            foreach (Match pm in PythonicRegex().Matches(masked))
            {
                var args = PythonicArgs.Parse(pm.Groups[2].Value);
                if (args is null)
                    continue;
                calls.Add(new LiftedToolCall(
                    $"llama_{calls.Count}",
                    pm.Groups[1].Value,
                    PythonicArgs.ToJson(args)));
                spans.Add((pm.Index, pm.Index + pm.Length));
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"<\|python_tag\|>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<\|python_tag\|>\s*(\{)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonStartRegex();

    [GeneratedRegex(@"<\|python_tag\|>\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\(([^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PythonicRegex();
}
