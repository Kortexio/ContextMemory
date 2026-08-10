using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects final answers about live external records (accounts, subscriptions, invoices, …)
/// when no successful MCP/wiki tool evidence exists for the turn.
/// </summary>
public static class AgenticLiveDataEvidenceGuardrail
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
        "account number"
    ];

    private static readonly string[] EvidenceToolMarkers =
    [
        "__",
        "query_objects",
        "zuora_graphql",
        "get_account",
        "manage_customer",
        "wiki_search",
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
            "Rejected: live-data question without successful MCP/wiki evidence. Emit tool_calls (e.g. query_objects); do not invent IDs or statuses.",
            "Rejeitado: pergunta de dados live sem evidência MCP/wiki bem-sucedida. Emite tool_calls (ex. query_objects); não inventes IDs nem estados.");
        return true;
    }

    public static bool IsLiveDataQuestion(string? userObjective, AppRuntimeConfig runtimeConfig)
    {
        var hasMcp = runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            i.Enabled
            && string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.IsConfigured);
        if (!hasMcp)
            return false;

        if (string.IsNullOrWhiteSpace(userObjective))
            return false;

        var text = userObjective.ToLowerInvariant();
        foreach (var marker in LiveDataMarkers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
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
}
