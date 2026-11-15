using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// DeepSeek R1-era sentinel format. Does NOT mask code fences — the argument payload is a
/// genuine <c>```json```</c> fence that is the envelope, not decorative example code.
/// Sentinels use U+FF5C (｜) and U+2581 (▁).
/// </summary>
public sealed partial class DeepSeekR1FamilyParser : IGrammarParser
{
    public string Id => "deepseek-r1";

    public bool Detect(string text) => DetectRegex().IsMatch(text);

    public GrammarParseResult Parse(string text, GrammarParseContext ctx)
    {
        if (!DetectRegex().IsMatch(text))
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        var spans = new List<(int Start, int End)>();
        var calls = new List<LiftedToolCall>();

        foreach (Match bm in SentinelBlockRegex().Matches(text))
        {
            var fromBlock = CallsFromSentinel(bm.Groups[1].Value, calls.Count);
            if (fromBlock.Count > 0)
            {
                calls.AddRange(fromBlock);
                spans.Add((bm.Index, bm.Index + bm.Length));
            }
        }

        if (calls.Count == 0)
        {
            foreach (Match m in BareCallRegex().Matches(text))
            {
                try
                {
                    var args = JsonSerializer.Deserialize<object>(m.Groups[2].Value.Trim());
                    calls.Add(new LiftedToolCall(
                        $"dsr1_{calls.Count}",
                        m.Groups[1].Value,
                        JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));
                    spans.Add((m.Index, m.Index + m.Length));
                }
                catch
                {
                    /* skip */
                }
            }
        }

        if (calls.Count == 0)
        {
            foreach (Match m in ToolsArrayRegex().Matches(text))
            {
                var fromArr = CallsFromToolsArray(m.Groups[1].Value, calls.Count);
                if (fromArr.Count > 0)
                {
                    calls.AddRange(fromArr);
                    spans.Add((m.Index, m.Index + m.Length));
                }
            }
        }

        var filtered = FamilySafety.RequireKnownTool(calls, ctx.ToolNames);
        if (filtered.Count == 0)
            return new GrammarParseResult(Array.Empty<LiftedToolCall>(), text);

        return new GrammarParseResult(filtered, FamilySafety.RemoveSpans(text, spans).Trim());
    }

    private static List<LiftedToolCall> CallsFromSentinel(string body, int startIndex)
    {
        var outCalls = new List<LiftedToolCall>();
        foreach (Match m in SentinelCallRegex().Matches(body))
        {
            try
            {
                var args = JsonSerializer.Deserialize<object>(m.Groups[2].Value.Trim());
                outCalls.Add(new LiftedToolCall(
                    $"dsr1_{startIndex + outCalls.Count}",
                    m.Groups[1].Value,
                    JsonSerializer.Serialize(args ?? new Dictionary<string, object?>())));
            }
            catch
            {
                /* malformed JSON inside the fence — skip */
            }
        }

        return outCalls;
    }

    private static List<LiftedToolCall> CallsFromToolsArray(string inner, int startIndex)
    {
        var outCalls = new List<LiftedToolCall>();
        try
        {
            using var doc = JsonDocument.Parse($"[{inner}]");
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameEl.GetString()!;
                JsonElement argsEl = default;
                var hasArgs = item.TryGetProperty("arguments", out argsEl)
                    || item.TryGetProperty("args", out argsEl);
                object? argsObj = hasArgs && argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? JsonSerializer.Deserialize<object>(argsEl.GetRawText())
                    : new Dictionary<string, object?>();
                outCalls.Add(new LiftedToolCall(
                    $"dsr1_{startIndex + outCalls.Count}",
                    name,
                    JsonSerializer.Serialize(argsObj ?? new Dictionary<string, object?>())));
            }
        }
        catch
        {
            /* not valid JSON once wrapped */
        }

        return outCalls;
    }

    // Patterns embed U+FF5C / U+2581 literally via char constants interpolated at regex build time.
    [GeneratedRegex(@"<\uFF5Ctool\u2581calls?\u2581begin\uFF5C>|function\s*<\uFF5Ctool\u2581sep\uFF5C>|""tools""\s*:\s*\[", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DetectRegex();

    [GeneratedRegex(@"<\uFF5Ctool\u2581calls\u2581begin\uFF5C>([\s\S]*?)<\uFF5Ctool\u2581calls\u2581end\uFF5C>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SentinelBlockRegex();

    [GeneratedRegex(@"<\uFF5Ctool\u2581call\u2581begin\uFF5C>\s*function\s*<\uFF5Ctool\u2581sep\uFF5C>\s*([A-Za-z0-9_.:-]+)\s*\n?```json\s*([\s\S]*?)```\s*<\uFF5Ctool\u2581call\u2581end\uFF5C>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SentinelCallRegex();

    [GeneratedRegex(@"function\s*<\uFF5Ctool\u2581sep\uFF5C>\s*([A-Za-z0-9_.:-]+)\s*\n?```json\s*([\s\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareCallRegex();

    [GeneratedRegex(@"\{\s*""tools""\s*:\s*\[([\s\S]*?)\]\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex ToolsArrayRegex();
}
