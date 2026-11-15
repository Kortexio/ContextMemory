using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// gpt-oss Harmony format leaked as plain text (<c>to=functions.NAME … {…}</c>).
/// </summary>
public sealed partial class HarmonyFamilyParser : IGrammarParser
{
    public string Id => "harmony";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match m in CallStartRegex().Matches(masked))
        {
            var openIndex = m.Index + m.Length - 1;
            var obj = FamilySafety.ExtractBalanced(masked, openIndex, '{', '}');
            if (obj is null)
                continue;

            try
            {
                var args = JsonSerializer.Deserialize<object>(obj.Value.Json);
                calls.Add(new LiftedToolCall(
                    $"harmony_{calls.Count}",
                    m.Groups[1].Value,
                    JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));

                var tail = TailMarkerRegex().Match(masked[obj.Value.EndIndex..]);
                var end = obj.Value.EndIndex + (tail.Success ? tail.Length : 0);
                spans.Add((m.Index, end));
            }
            catch
            {
                /* malformed JSON — leave as text */
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"to=functions\.[A-Za-z0-9_.:-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"to=functions\.([A-Za-z0-9_.:-]+)[^{]*?(\{)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallStartRegex();

    [GeneratedRegex(@"^\s*(?:<\|call\|>|<\|end\|>)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TailMarkerRegex();
}
