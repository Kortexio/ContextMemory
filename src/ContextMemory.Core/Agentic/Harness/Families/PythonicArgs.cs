using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>Shared pythonic call-arg parsing for llama / granite / olmo families.</summary>
public static partial class PythonicArgs
{
    public static Dictionary<string, object?>? Parse(string argsBody)
    {
        var parts = SplitTopLevel(argsBody);
        if (parts.Count == 0)
            return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                return null;
            var key = part[..eq].Trim();
            if (!IdentRegex().IsMatch(key))
                return null;
            var value = ParseLiteral(part[(eq + 1)..].Trim());
            result[key] = value;
        }

        return result;
    }

    public static string ToJson(Dictionary<string, object?> args) =>
        JsonSerializer.Serialize(args);

    public static List<string> SplitTopLevel(string args)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(args))
            return parts;

        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (quote is not null)
            {
                if (c == '\\' && i + 1 < args.Length) { i++; continue; }
                if (c == quote) quote = null;
                continue;
            }

            if (c is '"' or '\'') { quote = c; continue; }
            if (c is '(' or '[' or '{') { depth++; continue; }
            if (c is ')' or ']' or '}') { depth = Math.Max(0, depth - 1); continue; }
            if (c == ',' && depth == 0)
            {
                parts.Add(args[start..i].Trim());
                start = i + 1;
            }
        }

        var last = args[start..].Trim();
        if (last.Length > 0)
            parts.Add(last);
        return parts;
    }

    public static object? ParseLiteral(string raw)
    {
        if (raw is "True" or "true") return true;
        if (raw is "False" or "false") return false;
        if (raw is "None" or "null") return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && NumberRegex().IsMatch(raw))
            return n;
        if (raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\'')))
            return raw[1..^1];
        if (raw.StartsWith('['))
        {
            try
            {
                var rewritten = raw
                    .Replace("True", "true")
                    .Replace("False", "false")
                    .Replace("None", "null")
                    .Replace('\'', '"');
                return JsonSerializer.Deserialize<object>(rewritten);
            }
            catch { /* fallthrough */ }
        }

        return raw;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentRegex();

    [GeneratedRegex(@"^-?\d+(\.\d+)?([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
