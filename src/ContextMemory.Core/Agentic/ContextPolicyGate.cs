using System.Text.RegularExpressions;

namespace ContextMemory.Core.Agentic;

public sealed partial class ContextPolicyGate : Contracts.IContextPolicyGate
{
    public string FilterPromptSection(string content, ContextPolicy policy)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        policy ??= new ContextPolicy();
        var result = content;

        foreach (var pattern in policy.DeniedContextPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            try
            {
                result = Regex.Replace(
                    result,
                    pattern,
                    "[REDACTED]",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (RegexParseException)
            {
                // Treat as literal substring when pattern is not valid regex.
                result = result.Replace(pattern, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!policy.AllowConfidentialContent)
        {
            result = ConfidentialLineRegex().Replace(result, "[REDACTED confidential]");
        }

        if (policy.MaxStaticContextChars > 0 && result.Length > policy.MaxStaticContextChars)
        {
            result = result[..policy.MaxStaticContextChars] + "\n…[truncated by context policy]";
        }

        return result;
    }

    [GeneratedRegex(
        @"(?im)^.*\b(password|secret|api[_-]?key|token|credential|private[_-]?key)\b.*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConfidentialLineRegex();
}
