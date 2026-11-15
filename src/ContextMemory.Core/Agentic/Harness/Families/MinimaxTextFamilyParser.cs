using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// MiniMax-Text-01: call inside a fenced <c>```typescript</c> block. Does NOT mask fences —
/// the fence is the tool-call envelope.
/// </summary>
public sealed partial class MinimaxTextFamilyParser : IGrammarParser
{
    public string Id => "minimax-text";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        if (!DetectRegex().IsMatch(text))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match m in CallStartRegex().Matches(text))
        {
            var openIndex = m.Index + m.Length - 1;
            var obj = FamilySafety.ExtractBalanced(text, openIndex, '{', '}');
            if (obj is null)
                continue;
            var closeMatch = CloseRegex().Match(text[obj.Value.EndIndex..]);
            if (!closeMatch.Success)
                continue;
            try
            {
                var args = JsonSerializer.Deserialize<object>(obj.Value.Json);
                calls.Add(new LiftedToolCall(
                    $"minimaxtext_{calls.Count}",
                    m.Groups[1].Value,
                    JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));
                spans.Add((m.Index, obj.Value.EndIndex + closeMatch.Length));
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

    [GeneratedRegex(@"```typescript[\s\S]*?functions\.[A-Za-z0-9_.:-]+\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"```typescript\s*\n?\s*functions\.([A-Za-z0-9_.:-]+)\s*\(\s*(\{)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallStartRegex();

    [GeneratedRegex(@"^\s*\)\s*\n?```", RegexOptions.CultureInvariant)]
    private static partial Regex CloseRegex();
}
