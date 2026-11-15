using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Moonshot Kimi K2 sentinel tokens. Ids keep the <c>functions.{name}:{idx}</c> shape.
/// </summary>
public sealed partial class KimiFamilyParser : IGrammarParser
{
    public string Id => "kimi";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match sm in SectionRegex().Matches(masked))
        {
            var any = false;
            foreach (Match cm in CallRegex().Matches(sm.Groups[1].Value))
            {
                try
                {
                    var args = JsonSerializer.Deserialize<object>(cm.Groups[3].Value.Trim());
                    calls.Add(new LiftedToolCall(
                        $"functions.{cm.Groups[1].Value}:{cm.Groups[2].Value}",
                        cm.Groups[1].Value,
                        JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));
                    any = true;
                }
                catch
                {
                    /* skip malformed call inside the section */
                }
            }

            if (any)
                spans.Add((sm.Index, sm.Index + sm.Length));
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"<\|tool_calls?_section_(?:begin|end)\|>|<\|tool_call_(?:begin|end|argument_begin)\|>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<\|tool_calls?_section_begin\|>([\s\S]*?)<\|tool_calls?_section_end\|>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"<\|tool_call_begin\|>\s*functions\.([A-Za-z0-9_.:-]+)\s*:\s*(\d+)\s*<\|tool_call_argument_begin\|>([\s\S]*?)<\|tool_call_end\|>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallRegex();
}
