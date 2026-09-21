using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects answers that describe a URL/site without fetch evidence.
/// Markers and feedback come only from Admin <c>ConfigJson</c>
/// (<c>aboutSiteMarkers</c>, <c>fetchToolMarkers</c>, <c>feedback</c> — use <c>{hosts}</c> placeholder).
/// </summary>
public static partial class AgenticUrlFetchGuardrail
{
    public static bool TryGetRejectionFeedback(
        string? userObjective,
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(userObjective) || string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var aboutMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "aboutSiteMarkers");
        if (!RequiresUrlEvidence(userObjective, aboutMarkers))
            return false;

        var hosts = ExtractHosts(userObjective);
        if (hosts.Count == 0)
            return false;

        var fetchMarkers = AgenticGuardrailConfigReader.GetStringList(configJson, "fetchToolMarkers");
        if (HasFetchEvidence(hosts, steps, fetchMarkers))
            return false;

        if (HasFailedFetchAttempt(hosts, steps, fetchMarkers))
            return false;

        var hostList = string.Join(", ", hosts);
        var configured = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage);
        feedback = string.IsNullOrWhiteSpace(configured)
            ? AgenticGuardrailConfigReader.ResolveFeedback(configJson, runtimeConfig.DefaultLanguage)
            : configured.Replace("{hosts}", hostList, StringComparison.Ordinal);
        return true;
    }

    internal static bool RequiresUrlEvidence(string userObjective, IReadOnlyList<string> aboutSiteMarkers)
    {
        var text = userObjective.Trim();
        if (!HttpUrlRegex().IsMatch(text))
            return false;

        var lower = text.ToLowerInvariant();
        if (aboutSiteMarkers.Any(m =>
                !string.IsNullOrWhiteSpace(m)
                && lower.Contains(m.ToLowerInvariant(), StringComparison.Ordinal)))
            return true;

        var withoutUrls = HttpUrlRegex().Replace(text, " ").Trim();
        return withoutUrls.Length <= 120;
    }

    internal static IReadOnlyList<string> ExtractHosts(string text)
    {
        var hosts = new List<string>();
        foreach (Match match in HttpUrlRegex().Matches(text))
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("http" or "https"))
                continue;

            var host = uri.Host.Trim().ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host[4..];

            if (host.Length == 0)
                continue;

            if (!hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                hosts.Add(host);
        }

        return hosts;
    }

    private static bool HasFetchEvidence(
        IReadOnlyList<string> hosts,
        IReadOnlyList<AgentExecutionStep> steps,
        IReadOnlyList<string> fetchMarkers)
    {
        foreach (var step in steps)
        {
            if (!step.Success)
                continue;

            var blob = $"{step.ToolName}\n{step.Arguments}\n{step.Output}";
            if (!HostsMentioned(blob, hosts))
                continue;

            if (LooksLikeFetchTool(step.ToolName, blob, fetchMarkers))
                return true;
        }

        return false;
    }

    private static bool HasFailedFetchAttempt(
        IReadOnlyList<string> hosts,
        IReadOnlyList<AgentExecutionStep> steps,
        IReadOnlyList<string> fetchMarkers)
    {
        foreach (var step in steps)
        {
            if (step.Success)
                continue;

            var blob = $"{step.ToolName}\n{step.Arguments}\n{step.Output}";
            if (HostsMentioned(blob, hosts) && LooksLikeFetchTool(step.ToolName, blob, fetchMarkers))
                return true;
        }

        return false;
    }

    private static bool HostsMentioned(string blob, IReadOnlyList<string> hosts)
    {
        foreach (var host in hosts)
        {
            if (blob.Contains(host, StringComparison.OrdinalIgnoreCase))
                return true;
            if (blob.Contains("www." + host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool LooksLikeFetchTool(string toolName, string blob, IReadOnlyList<string> fetchMarkers)
    {
        var lowerTool = toolName.ToLowerInvariant();
        if (fetchMarkers.Any(m =>
                !string.IsNullOrWhiteSpace(m)
                && lowerTool.Contains(m.ToLowerInvariant(), StringComparison.Ordinal)))
            return true;

        var lowerBlob = blob.ToLowerInvariant();
        return fetchMarkers.Any(m =>
            !string.IsNullOrWhiteSpace(m)
            && lowerBlob.Contains(m.ToLowerInvariant(), StringComparison.Ordinal));
    }

    [GeneratedRegex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}
