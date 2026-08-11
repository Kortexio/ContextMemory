using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects final answers about live external records (accounts, subscriptions, invoices, tickets, …)
/// when no successful MCP/wiki tool evidence exists for the turn.
/// After evidence tools were attempted, allows honest "not found / tool failed" disclosures
/// so the loop cannot reject forever when data is missing.
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

    private static readonly string[] HonestUnknownMarkers =
    [
        "não encontrei",
        "nao encontrei",
        "não foi possível",
        "nao foi possivel",
        "sem resultados",
        "sem evidência",
        "sem evidencia",
        "não há dados",
        "nao ha dados",
        "not found",
        "no results",
        "no evidence",
        "could not find",
        "couldn't find",
        "unable to find",
        "no matching",
        "empty result",
        "tool failed",
        "tool error",
        "falhou",
        "failed"
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

        // Tools were tried (and failed / returned nothing useful to the model): allow an honest disclosure
        // instead of looping forever on RequireEvidence.
        if (HasEvidenceAttempt(steps) && LooksLikeHonestUnknown(finalAnswer))
            return false;

        feedback = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Rejected: live-data/wiki question without successful MCP/wiki evidence. "
            + "Emit tool_calls now — prefer wiki_search/wiki_grep for tickets/docs, or MCP query_objects for Zuora. "
            + "Do not invent IDs or statuses. Example: {\"tool\":\"wiki_search\",\"arguments\":{\"query\":\"PAC-759\"}}",
            "Rejeitado: pergunta live/wiki sem evidência MCP/wiki bem-sucedida. "
            + "Emite tool_calls agora — prefere wiki_search/wiki_grep para tickets/docs, ou MCP query_objects para Zuora. "
            + "Não inventes IDs nem estados. Exemplo: {\"tool\":\"wiki_search\",\"arguments\":{\"query\":\"PAC-759\"}}");
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

    private static bool HasSuccessfulEvidence(IReadOnlyList<AgentExecutionStep> steps) =>
        steps.Any(s => s.Success && IsEvidenceTool(s.ToolName));

    private static bool HasEvidenceAttempt(IReadOnlyList<AgentExecutionStep> steps) =>
        steps.Any(s => IsEvidenceTool(s.ToolName));

    private static bool IsEvidenceTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName) || SessionDiscoveryTools.IsDiscoveryTool(toolName))
            return false;

        foreach (var marker in EvidenceToolMarkers)
        {
            if (toolName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return toolName.Contains("__", StringComparison.Ordinal);
    }

    private static bool LooksLikeHonestUnknown(string finalAnswer)
    {
        foreach (var marker in HonestUnknownMarkers)
        {
            if (finalAnswer.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Jira-style keys such as PAC-759, ABC-12.</summary>
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRegex();
}
