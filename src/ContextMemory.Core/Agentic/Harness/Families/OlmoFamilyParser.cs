using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// OLMo3: <c>&lt;function_calls&gt;</c> wrapping one pythonic call per line.
/// </summary>
public sealed partial class OlmoFamilyParser : IGrammarParser
{
    public string Id => "olmo";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match bm in BlockRegex().Matches(masked))
        {
            var any = false;
            foreach (var line in bm.Groups[1].Value.Split('\n'))
            {
                var lm = LineRegex().Match(line);
                if (!lm.Success)
                    continue;
                var args = PythonicArgs.Parse(lm.Groups[2].Value);
                if (args is null)
                    continue;
                calls.Add(new LiftedToolCall(
                    $"olmo_{calls.Count}",
                    lm.Groups[1].Value,
                    PythonicArgs.ToJson(args)));
                any = true;
            }

            if (any)
                spans.Add((bm.Index, bm.Index + bm.Length));
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"<function_calls>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<function_calls>([\s\S]*?)</function_calls>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_.]*)\(([^)]*)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();
}
