using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// CodeForge phantomToolUse: assistant announces a catalog tool in prose but emitted no tool_calls.
/// </summary>
public static partial class PhantomToolUse
{
    public const int DefaultRetryCap = 2;

    public sealed record Result(bool IsPhantom, string? MatchedToolName, string? Reason);

    public static Result Detect(string? text, bool hasToolCalls, IReadOnlySet<string> toolNames)
    {
        if (hasToolCalls || toolNames.Count == 0 || string.IsNullOrWhiteSpace(text))
            return new Result(false, null, null);

        var folded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in toolNames)
            folded[Fold(t)] = t;

        var bare = text.Trim().Trim('`', '\'', '"', '*', '_', '.', '!', '?', ':');
        if (bare.Length is > 0 and <= 64 && folded.TryGetValue(Fold(bare), out var bareMatch))
        {
            return new Result(true, bareMatch, $"replied with the tool name \"{bare}\" instead of calling it");
        }

        foreach (Match m in AnnounceRegex().Matches(text))
        {
            var raw = m.Groups[1].Value;
            if (folded.TryGetValue(Fold(raw), out var matched))
                return new Result(true, matched, $"announced \"{raw}\" without emitting a tool call");
        }

        return new Result(false, null, null);
    }

    public static bool ShouldRetry(int retriesSoFar, int cap = DefaultRetryCap) =>
        retriesSoFar < cap;

    public static string BuildRetryPrompt(string toolName) =>
        $"You said you would call {toolName}, but no tool call was received in the expected format. "
        + $"Call {toolName} now using the tool-calling mechanism (exact catalog name), "
        + "or continue without it if it is no longer needed.";

    private static string Fold(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray()).ToLowerInvariant();

    [GeneratedRegex(
        @"\b(?:I(?:'| wi)ll|I am going to|I'm going to|Let me|Vou|Vou agora|Irei)\s+(?:now\s+)?(?:call|use|invoke|run|chamar|usar|invocar|executar)\s+(?:the\s+|a\s+(?:ferramenta|tool)\s+)?[""'`]?([A-Za-z0-9_][A-Za-z0-9_.:-]*)[""'`]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnnounceRegex();
}
