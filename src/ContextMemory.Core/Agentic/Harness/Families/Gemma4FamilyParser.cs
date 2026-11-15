using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Gemma 4 marker (<c>&lt;|tool_call&gt;call:Name{args}&lt;tool_call|&gt;</c>).
/// </summary>
public sealed partial class Gemma4FamilyParser : IGrammarParser
{
    public string Id => "gemma4";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match m in CallRegex().Matches(masked))
        {
            var name = m.Groups[1].Value;
            var args = TryParseArgs(m.Groups[2].Value);
            calls.Add(new LiftedToolCall(
                $"gemma4_{calls.Count}",
                name,
                args is not null
                    ? JsonSerializer.Serialize(args)
                    : JsonSerializer.Serialize(new Dictionary<string, object?> { ["__invalidJson"] = m.Groups[2].Value })));
            spans.Add((m.Index, m.Index + m.Length));
        }

        if (calls.Count == 0)
        {
            var dm = DanglingRegex().Match(masked);
            if (dm.Success)
            {
                var args = TryParseArgs(dm.Groups[2].Value);
                if (args is not null)
                {
                    calls.Add(new LiftedToolCall("gemma4_0", dm.Groups[1].Value, JsonSerializer.Serialize(args)));
                    spans.Add((dm.Index, dm.Index + dm.Length));
                }
                else
                {
                    var cleaned = FamilySafety.RemoveSpans(
                        text,
                        new[] { (dm.Index, dm.Index + dm.Length) }).Trim();
                    return new GrammarParseResult(Array.Empty<LiftedToolCall>(), cleaned);
                }
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    private static object? TryParseArgs(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(raw);
        }
        catch
        {
            /* fall through to light repair */
        }

        var repaired = BareKeyRegex().Replace(raw, "$1\"$2\":");
        repaired = TrailingCommaRegex().Replace(repaired, "$1");
        try
        {
            return JsonSerializer.Deserialize<object>(repaired);
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"<\|tool_call>\s*call\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<\|tool_call>\s*call\s*:\s*([A-Za-z0-9_.:-]+)\s*(\{[\s\S]*?\})\s*<tool_call\|>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallRegex();

    [GeneratedRegex(@"<\|tool_call>\s*call\s*:\s*([A-Za-z0-9_.:-]+)\s*(\{[\s\S]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DanglingRegex();

    [GeneratedRegex(@"([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex BareKeyRegex();

    [GeneratedRegex(@",\s*([}\]])", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCommaRegex();
}
