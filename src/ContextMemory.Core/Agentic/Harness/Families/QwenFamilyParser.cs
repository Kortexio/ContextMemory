using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Qwen / Hermes family (<c>&lt;tool_call&gt;</c> + <c>&lt;function=…&gt;</c> + <c>&lt;parameter=…&gt;</c>).
/// </summary>
public sealed partial class QwenFamilyParser : IGrammarParser
{
    public string Id => "qwen";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        var masked = FamilySafety.MaskCodeFences(text);
        if (!DetectRegex().IsMatch(masked))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var repairedMasked = RepairMergedTags(masked);
        var calls = XmlToolCallParser.LooksLikeXmlToolMarkup(repairedMasked)
            ? XmlToolCallParser.ParseXmlToolCalls(repairedMasked)
            : Array.Empty<LiftedToolCall>();
        calls = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);

        if (calls.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var spans = new List<(int Start, int End)>();
        foreach (Match m in StripBlockRegex().Matches(masked))
            spans.Add((m.Index, m.Index + m.Length));

        if (spans.Count == 0)
        {
            var open = OpenToolCallTailRegex().Match(masked);
            if (open.Success)
                spans.Add((open.Index, open.Index + open.Length));
        }

        var cleanedText = spans.Count > 0
            ? FamilySafety.RemoveSpans(text, spans).Trim()
            : text;
        return new GrammarParseResult(calls, cleanedText);
    }

    private static string RepairMergedTags(string text) =>
        MergedParameterRegex().Replace(
            MergedFunctionRegex().Replace(text, "$1<function=$2>"),
            "$1<parameter=$2>");

    [GeneratedRegex(@"<tool_call\b|<function\s*=|<parameter\s*=|\bfunction\s*=\s*[A-Za-z0-9_.:-]+>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"(^|[^<])\bfunction\s*=\s*([A-Za-z0-9_.:-]+)>", RegexOptions.CultureInvariant)]
    private static partial Regex MergedFunctionRegex();

    [GeneratedRegex(@"(^|[^<])\bparameter\s*=\s*([A-Za-z0-9_]+)>", RegexOptions.CultureInvariant)]
    private static partial Regex MergedParameterRegex();

    [GeneratedRegex(@"<tool_call\b[^>]*>[\s\S]*?</tool_call>|<function\s*=[\s\S]*?</function>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StripBlockRegex();

    [GeneratedRegex(@"<tool_call\b[^>]*>[\s\S]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenToolCallTailRegex();
}
