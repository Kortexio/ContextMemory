using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects final answers about live external records (accounts, subscriptions, invoices, tickets, …)
/// when no successful MCP/wiki tool evidence exists for the turn.
/// </summary>
public static partial class AgenticLiveDataEvidenceGuardrail
{
    private static readonly string[] LiveDataMarkers =
    [
        "account",
        "conta",
        "subscription",
        "assinatura",
        "invoice",
        "fatura",
        "payment",
        "pagamento",
        "zuora",
        "billing",
        "canceled",
        "cancelled",
        "cancelad",
        "customer",
        "cliente",
        "balance",
        "saldo",
        "rate plan",
        "query_objects",
        "accountnumber",
        "account number",
        "ticket",
        "tickets",
        "jira",
        "issue",
        "issues",
        "confluence",
        "wiki"
    ];

    private static readonly string[] EvidenceToolMarkers =
    [
        "__",
        "query_objects",
        "zuora_graphql",
        "get_account",
        "manage_customer",
        "wiki_search",
        "wiki_grep",
        "wiki_get",
        "wiki_read"
    ];

    public static bool TryGetRejectionFeedback(
        string? userObjective,
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!IsLiveDataQuestion(userObjective, runtimeConfig))
            return false;

        if (HasSuccessfulEvidence(steps))
            return false;

        feedback = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Rejected: live-data / wiki question without successful MCP/wiki evidence. Emit tool_calls (e.g. wiki_search or query_objects); do not invent IDs, tickets, or statuses.",
            "Rejeitado: pergunta de dados live/wiki sem evidência MCP/wiki bem-sucedida. Emite tool_calls (ex. wiki_search ou query_objects); não inventes IDs, tickets nem estados.");
        return true;
    }

    public static bool IsLiveDataQuestion(string? userObjective, AppRuntimeConfig runtimeConfig)
    {
        if (!HasEvidenceBackend(runtimeConfig))
            return false;

        if (string.IsNullOrWhiteSpace(userObjective))
            return false;

        if (IssueKeyRegex().IsMatch(userObjective))
            return true;

        var text = userObjective.ToLowerInvariant();
        foreach (var marker in LiveDataMarkers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasEvidenceBackend(AppRuntimeConfig runtimeConfig)
    {
        if (runtimeConfig.GlobalWikiEnabled)
            return true;

        return runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            i.Enabled
            && string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.IsConfigured);
    }

    private static bool HasSuccessfulEvidence(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (!step.Success)
                continue;
            if (SessionDiscoveryTools.IsDiscoveryTool(step.ToolName))
                continue;

            var name = step.ToolName ?? string.Empty;
            foreach (var marker in EvidenceToolMarkers)
            {
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Any successful MCP-qualified tool (server__tool).
            if (name.Contains("__", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Jira-style keys such as PAC-759, ABC-12.</summary>
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();
}
