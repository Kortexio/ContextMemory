using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// DeepSeek DSML XML (V3.2 / V4-Pro / V4-Flash). Fullwidth-bar and ASCII-pipe variants.
/// </summary>
public sealed partial class DsmlFamilyParser : IGrammarParser
{
    public string Id => "dsml";

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
            foreach (Match im in InvokeBlockRegex().Matches(bm.Groups[1].Value))
            {
                any = true;
                calls.Add(new LiftedToolCall(
                    $"dsml_{calls.Count}",
                    im.Groups[1].Value,
                    JsonSerializer.Serialize(ParseParams(im.Groups[2].Value))));
            }

            if (any)
                spans.Add((bm.Index, bm.Index + bm.Length));
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
        {
            var dangling = DanglingRegex().Match(masked);
            if (dangling.Success && !ToolCallsOpenTagRegex().IsMatch(masked[..dangling.Index]))
            {
                var cleaned = FamilySafety.RemoveSpans(
                    text,
                    new[] { (dangling.Index, dangling.Index + dangling.Length) }).Trim();
                return new GrammarParseResult(Array.Empty<LiftedToolCall>(), cleaned);
            }

            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);
        }

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    private static Dictionary<string, object?> ParseParams(string body)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (Match m in ParamRegex().Matches(body))
        {
            var name = m.Groups[1].Value;
            var isJson = m.Groups[2].Value == "false";
            var raw = m.Groups[3].Value;
            if (raw.StartsWith('\r') || raw.StartsWith('\n'))
                raw = raw.TrimStart('\r', '\n');
            if (raw.EndsWith('\r') || raw.EndsWith('\n'))
                raw = raw.TrimEnd('\r', '\n');

            if (isJson)
            {
                try
                {
                    args[name] = JsonSerializer.Deserialize<object>(raw.Trim());
                    continue;
                }
                catch
                {
                    /* keep as string */
                }
            }

            args[name] = raw;
        }

        return args;
    }

    // BAR = (?:\uFF5C{1,2}|\|), WRAP = (?:tool_calls?|function_calls?)
    [GeneratedRegex(@"(?:\uFF5C{1,2}|\|)\s*DSML|DSML\s*(?:\uFF5C{1,2}|\|)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*(?:tool_calls?|function_calls?)\s*>([\s\S]*?)<\s*/\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*(?:tool_calls?|function_calls?)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*invoke\s+name\s*=\s*""([^""]+)""\s*>([\s\S]*?)<\s*/\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*invoke\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvokeBlockRegex();

    [GeneratedRegex(@"<\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*parameter\s+name\s*=\s*""([^""]+)""(?:\s+string\s*=\s*""(true|false)"")?\s*>([\s\S]*?)<\s*/\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*parameter\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParamRegex();

    [GeneratedRegex(@"<\s*(?:\uFF5C{1,2}|\|)?\s*DSML[\s\S]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DanglingRegex();

    [GeneratedRegex(@"<\s*(?:\uFF5C{1,2}|\|)?\s*DSML\s*(?:\uFF5C{1,2}|\|)\s*tool_calls?\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolCallsOpenTagRegex();
}
