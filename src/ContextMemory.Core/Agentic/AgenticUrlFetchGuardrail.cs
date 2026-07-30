using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects answers that describe an external web page/URL without any tool evidence
/// that the page (or its host) was actually fetched/searched.
/// </summary>
public static partial class AgenticUrlFetchGuardrail
{
    private static readonly string[] AboutSiteMarkers =
    [
        "esse site",
        "este site",
        "o site",
        "esse link",
        "este link",
        "esta página",
        "esta pagina",
        "essa página",
        "essa pagina",
        "this site",
        "this website",
        "this page",
        "this url",
        "this link",
        "the website",
        "the site",
        "sobre o que",
        "do que se trata",
        "o que é",
        "o que e",
        "what is",
        "what's this",
        "whats this",
        "what about",
        "abre",
        "abrir",
        "visita",
        "visitar",
        "open ",
        "visit ",
        "fetch",
        "scrape",
        "resumo",
        "summary",
        "conteúdo",
        "conteudo",
        "content of"
    ];

    private static readonly string[] FetchToolMarkers =
    [
        "python_execute",
        "shell_execute",
        "node_execute",
        "web_search",
        "brave",
        "tavily",
        "ddgs",
        "duckduckgo",
        "playwright",
        "httpx",
        "requests",
        "curl"
    ];

    public static bool TryGetRejectionFeedback(
        string? userObjective,
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(userObjective) || string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!RequiresUrlEvidence(userObjective))
            return false;

        var hosts = ExtractHosts(userObjective);
        if (hosts.Count == 0)
            return false;

        if (HasFetchEvidence(hosts, steps))
            return false;

        // If a fetch was attempted and failed, allow the model to report the failure.
        if (HasFailedFetchAttempt(hosts, steps))
            return false;

        feedback = BuildFeedback(runtimeConfig, hosts);
        return true;
    }

    internal static bool RequiresUrlEvidence(string userObjective)
    {
        var text = userObjective.Trim();
        if (!HttpUrlRegex().IsMatch(text))
            return false;

        var lower = text.ToLowerInvariant();
        if (AboutSiteMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal)))
            return true;

        // Short prompts that are mostly a URL + short question (e.g. "e este?\nhttps://…")
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

    private static bool HasFetchEvidence(IReadOnlyList<string> hosts, IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (!step.Success)
                continue;

            var blob = $"{step.ToolName}\n{step.Arguments}\n{step.Output}";
            if (!HostsMentioned(blob, hosts))
                continue;

            if (LooksLikeFetchTool(step.ToolName, blob))
                return true;
        }

        return false;
    }

    private static bool HasFailedFetchAttempt(IReadOnlyList<string> hosts, IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Success)
                continue;

            var blob = $"{step.ToolName}\n{step.Arguments}\n{step.Output}";
            if (HostsMentioned(blob, hosts) && LooksLikeFetchTool(step.ToolName, blob))
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

    private static bool LooksLikeFetchTool(string toolName, string blob)
    {
        var lowerTool = toolName.ToLowerInvariant();
        if (FetchToolMarkers.Any(m => lowerTool.Contains(m, StringComparison.Ordinal)))
            return true;

        var lowerBlob = blob.ToLowerInvariant();
        return FetchToolMarkers.Any(m => lowerBlob.Contains(m, StringComparison.Ordinal));
    }

    private static string BuildFeedback(AppRuntimeConfig config, IReadOnlyList<string> hosts)
    {
        var hostList = string.Join(", ", hosts);
        return TenantLocale.Select(
            config.DefaultLanguage,
            "Rejected: you described a website/URL without fetching it. "
            + $"Hosts in the user message: {hostList}. "
            + "You do NOT know page content from memory. Emit tool_calls first — e.g. `python_execute` with httpx/BeautifulSoup "
            + "or Playwright (JS pages), or a gateway web-search tool — then answer ONLY from the tool output. "
            + "Do not invent product purpose, APIs, or comparisons.",
            "Rejeitado: descreveste um site/URL sem o ires buscar. "
            + $"Hosts na mensagem do utilizador: {hostList}. "
            + "NÃO conheces o conteúdo da página de memória. Emite tool_calls primeiro — p.ex. `python_execute` com httpx/BeautifulSoup "
            + "ou Playwright (páginas com JS), ou uma tool de web-search do gateway — e responde APENAS com base no output da tool. "
            + "Não inventes propósito do produto, APIs ou comparações.");
    }

    [GeneratedRegex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}
