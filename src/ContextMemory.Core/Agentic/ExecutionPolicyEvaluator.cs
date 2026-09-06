using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Agentic;

public sealed partial class ExecutionPolicyEvaluator : IExecutionPolicyEvaluator
{
    private static readonly string[] NetworkToolHints =
    [
        "http", "fetch", "url", "web", "browser", "download", "egress", "request"
    ];

    public ExecutionPolicyDecision EvaluateTool(
        string toolName,
        string? arguments,
        ExecutionPolicy policy)
    {
        policy ??= new ExecutionPolicy();
        toolName ??= string.Empty;
        arguments ??= string.Empty;

        var egress = (policy.NetworkEgress ?? "restricted").Trim().ToLowerInvariant();
        var looksNetwork = LooksLikeNetworkTool(toolName, arguments);

        if (egress == "deny" && looksNetwork)
            return ExecutionPolicyDecision.Deny;

        if (egress == "restricted" && looksNetwork && HasDisallowedHost(arguments, policy.AllowedEgressHosts))
            return ExecutionPolicyDecision.Deny;

        if (FindConfirmationMatch(toolName, arguments, policy.RequireConfirmationFor) is not null)
            return ExecutionPolicyDecision.RequireConfirm;

        return ExecutionPolicyDecision.Allow;
    }

    /// <summary>Returns the matched require-confirmation pattern, if any.</summary>
    public static string? FindConfirmationMatch(
        string toolName,
        string? arguments,
        IReadOnlyList<string> requireConfirmationFor)
    {
        arguments ??= string.Empty;
        foreach (var pattern in requireConfirmationFor)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (PolicyGlob.IsMatch(toolName, pattern)
                || PolicyGlob.IsMatch(arguments, pattern)
                || toolName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || arguments.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return pattern.Trim();
            }
        }

        return null;
    }

    private static bool LooksLikeNetworkTool(string toolName, string arguments)
    {
        foreach (var hint in NetworkToolHints)
        {
            if (toolName.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return UrlRegex().IsMatch(arguments);
    }

    private static bool HasDisallowedHost(string arguments, IReadOnlyList<string> allowedHosts)
    {
        if (allowedHosts is null || allowedHosts.Count == 0)
            return false; // unrestricted host list under restricted mode = allow (existing default)

        var matches = UrlHostRegex().Matches(arguments);
        if (matches.Count == 0)
            return false;

        foreach (Match match in matches)
        {
            var host = match.Groups[1].Value;
            var allowed = false;
            foreach (var pattern in allowedHosts)
            {
                if (PolicyGlob.IsMatch(host, pattern)
                    || host.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("." + pattern.TrimStart('*', '.'), StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"https?://[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"https?://([^/\s""':]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlHostRegex();
}
