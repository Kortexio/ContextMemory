using System.Text;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

public static partial class FamilySafety
{
    public static string MaskCodeFences(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return CodeFenceRegex().Replace(text, m =>
        {
            var sb = new StringBuilder(m.Length);
            foreach (var ch in m.Value)
                sb.Append(ch == '\n' ? '\n' : ' ');
            return sb.ToString();
        });
    }

    public static IReadOnlyList<LiftedToolCall> RequireKnownTool(
        IReadOnlyList<LiftedToolCall> calls,
        IReadOnlySet<string> toolNames)
    {
        if (toolNames.Count == 0)
            return calls;
        return calls
            .Where(c => toolNames.Contains(c.Name))
            .ToList();
    }

    public static string RemoveSpans(string text, IReadOnlyList<(int Start, int End)> spans)
    {
        if (spans.Count == 0)
            return text;
        var ordered = spans.OrderByDescending(s => s.Start).ToList();
        var s = text;
        foreach (var (start, end) in ordered)
        {
            if (start < 0 || end > s.Length || start >= end)
                continue;
            s = s[..start] + s[end..];
        }

        return s;
    }

    public static (string Json, int EndIndex)? ExtractBalanced(string text, int openIndex, char open, char close)
    {
        if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != open)
            return null;
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                    return (text[openIndex..(i + 1)], i + 1);
            }
        }

        return null;
    }

    public static object? CoerceScalar(string raw)
    {
        var v = raw.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
            && Regex.IsMatch(v, @"^-?\d+(\.\d+)?([eE][+-]?\d+)?$"))
            return n;
        if ((v.StartsWith('{') && v.EndsWith('}')) || (v.StartsWith('[') && v.EndsWith(']')))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<object>(v); }
            catch { /* keep string */ }
        }

        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
            return v[1..^1];
        return v;
    }

    public static IReadOnlyList<LiftedToolCall> DedupeIdentical(IReadOnlyList<LiftedToolCall> calls)
    {
        if (calls.Count <= 1)
            return calls;
        var result = new List<LiftedToolCall> { calls[0] };
        for (var i = 1; i < calls.Count; i++)
        {
            var prev = result[^1];
            var cur = calls[i];
            if (prev.Name.Equals(cur.Name, StringComparison.Ordinal)
                && prev.ArgumentsJson.Equals(cur.ArgumentsJson, StringComparison.Ordinal))
                continue;
            result.Add(cur);
        }

        return result;
    }

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.CultureInvariant)]
    private static partial Regex CodeFenceRegex();
}
