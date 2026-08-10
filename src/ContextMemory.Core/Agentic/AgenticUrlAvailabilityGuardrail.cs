using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static partial class AgenticUrlAvailabilityGuardrail
{
    public static async Task<(bool Reject, string Feedback)> TryGetRejectionFeedbackAsync(
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        IAgenticUrlAvailabilityChecker? checker,
        CancellationToken cancellationToken = default)
    {
        if (checker is null || string.IsNullOrWhiteSpace(finalAnswer))
            return (false, string.Empty);

        var urls = ExtractHttpUrls(finalAnswer);
        if (urls.Count == 0)
            return (false, string.Empty);

        var publicUrls = new List<string>();
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            if (HostAllowlist.IsBlockedHostOrIp(uri.Host))
                continue;

            publicUrls.Add(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') == uri.GetLeftPart(UriPartial.Authority)
                ? uri.AbsoluteUri
                : uri.AbsoluteUri);
        }

        if (publicUrls.Count == 0)
            return (false, string.Empty);

        var timeoutMs = AgenticGuardrailConfigReader.GetInt(configJson, "timeoutMs", 3000);
        var dead = await checker.FindUnreachableUrlsAsync(publicUrls, timeoutMs, cancellationToken)
            .ConfigureAwait(false);

        if (dead.Count == 0)
            return (false, string.Empty);

        var feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
            ?? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                $"Rejected: unreachable URL(s): {string.Join(", ", dead)}.",
                $"Rejeitado: URL(s) inacessível(eis): {string.Join(", ", dead)}.");
        return (true, feedback);
    }

    public static IReadOnlyList<string> ExtractHttpUrls(string text)
    {
        var list = new List<string>();
        foreach (Match m in HttpUrlRegex().Matches(text))
        {
            var u = m.Value.TrimEnd(')', ']', '.', ',', ';', '"', '\'');
            if (!list.Contains(u, StringComparer.OrdinalIgnoreCase))
                list.Add(u);
        }

        return list;
    }

    [GeneratedRegex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}
