using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Anthropic-style <c>&lt;invoke&gt;</c> XML (MiniMax wrapper or bare), plus M2 bracket-dropped repair.
/// </summary>
public sealed partial class MinimaxFamilyParser : IGrammarParser
{
    public string Id => "minimax";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(RepairBrokenAngles(masked)))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var repairedMasked = RepairBrokenAngles(masked);
        var repairedOriginal = RepairBrokenAngles(text);

        var calls = new List<LiftedToolCall>();
        var spans = new List<(int Start, int End)>();

        foreach (Match wm in WrapRegex().Matches(repairedMasked))
        {
            var fromWrap = ParseInvokes(wm.Groups[1].Value, calls.Count);
            if (fromWrap.Count > 0)
            {
                calls.AddRange(fromWrap);
                spans.Add((wm.Index, wm.Index + wm.Length));
            }
        }

        if (calls.Count == 0)
        {
            foreach (Match im in InvokeRegex().Matches(repairedMasked))
            {
                var fromBare = ParseInvokes(im.Value, calls.Count);
                if (fromBare.Count > 0)
                {
                    calls.AddRange(fromBare);
                    spans.Add((im.Index, im.Index + im.Length));
                }
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(repairedOriginal, spans).Trim());
    }

    private static string RepairBrokenAngles(string text)
    {
        var s = BrokenInvokeOpenRegex().Replace(text, "$1<invoke name=\"$2\">");
        s = BrokenParameterOpenRegex().Replace(s, "$1<parameter name=\"$2\">");
        s = BrokenParameterCloseRegex().Replace(s, "</parameter>");
        s = BrokenInvokeCloseRegex().Replace(s, "</invoke>");
        return s;
    }

    private static List<LiftedToolCall> ParseInvokes(string body, int startIndex)
    {
        var outCalls = new List<LiftedToolCall>();
        foreach (Match im in InvokeRegex().Matches(body))
        {
            var args = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (Match pm in ParamRegex().Matches(im.Groups[2].Value))
                args[pm.Groups[1].Value] = FamilySafety.CoerceScalar(pm.Groups[2].Value);

            outCalls.Add(new LiftedToolCall(
                $"minimax_{startIndex + outCalls.Count}",
                im.Groups[1].Value,
                JsonSerializer.Serialize(args)));
        }

        return outCalls;
    }

    [GeneratedRegex(@"<minimax:tool_call>|<invoke\s+name\s*=|invoke\s+name\s*=\s*""[^""]+""\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<minimax:tool_call>([\s\S]*?)</minimax:tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WrapRegex();

    [GeneratedRegex(@"<invoke\s+name\s*=\s*""([^""]+)""\s*>([\s\S]*?)</invoke>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvokeRegex();

    [GeneratedRegex(@"<parameter\s+name\s*=\s*""([^""]+)""\s*>([\s\S]*?)</parameter>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParamRegex();

    [GeneratedRegex(@"(^|[^<])\binvoke\s+name\s*=\s*""([^""]+)""\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenInvokeOpenRegex();

    [GeneratedRegex(@"(^|[^<])\bparameter\s+name\s*=\s*""([^""]+)""\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenParameterOpenRegex();

    [GeneratedRegex(@"(?<![</])\bparameter>", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenParameterCloseRegex();

    [GeneratedRegex(@"(?<![</])\binvoke>", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenInvokeCloseRegex();
}
