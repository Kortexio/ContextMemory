using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Zhipu/Z.ai GLM: <c>&lt;tool_call&gt;name\n&lt;arg_key&gt;…&lt;/arg_key&gt;&lt;arg_value&gt;…&lt;/arg_value&gt;&lt;/tool_call&gt;</c>.
/// </summary>
public sealed partial class GlmFamilyParser : IGrammarParser
{
    public string Id => "glm";

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
            var body = bm.Groups[1].Value;
            var firstArg = ArgRegex().Match(body);
            var nameRaw = (firstArg.Success ? body[..firstArg.Index] : body).Trim();
            if (!NameRegex().IsMatch(nameRaw))
                continue;

            var args = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (Match am in ArgRegex().Matches(body))
                args[am.Groups[1].Value.Trim()] = FamilySafety.CoerceScalar(am.Groups[2].Value);

            calls.Add(new LiftedToolCall($"glm_{calls.Count}", nameRaw, JsonSerializer.Serialize(args)));
            spans.Add((bm.Index, bm.Index + bm.Length));
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    [GeneratedRegex(@"<tool_call>[\s\S]*?</tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<tool_call>([\s\S]*?)</tool_call>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<arg_key>([\s\S]*?)</arg_key>\s*<arg_value>([\s\S]*?)</arg_value>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArgRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
