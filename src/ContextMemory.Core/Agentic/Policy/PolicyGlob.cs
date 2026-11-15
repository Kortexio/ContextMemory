using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic;

/// <summary>Simple glob matching: <c>*</c> matches any sequence; case-insensitive.</summary>
internal static class PolicyGlob
{
    public static bool IsMatch(string? value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        value ??= string.Empty;
        pattern = pattern.Trim();

        if (pattern == "*")
            return true;

        if (!pattern.Contains('*', StringComparison.Ordinal))
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MatchesAny(string? value, IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return false;

        foreach (var pattern in patterns)
        {
            if (IsMatch(value, pattern))
                return true;
        }

        return false;
    }
}
