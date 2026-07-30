using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Rejects model answers that invent false sandbox limitations
/// (e.g. "Azure Container Apps has no network") when the tenant actually uses
/// self-hosted-sandbox with outbound HTTP.
/// </summary>
public static class AgenticSandboxClaimGuardrail
{
    private static readonly string[] AcaMarkers =
    [
        "azure container apps",
        "azure container app",
        "aca dynamic session",
        "aca session",
        "ambiente isolado (aca)",
        "isolated azure container"
    ];

    private static readonly string[] NoNetworkMarkers =
    [
        "não tem acesso à rede",
        "nao tem acesso a rede",
        "sem acesso à rede",
        "sem acesso a rede",
        "não tem acesso a rede",
        "no access to the network",
        "no network access",
        "without network access",
        "cannot access the network",
        "can't access the network",
        "network egress",
        "rede externa",
        "external network",
        "dns/timeout",
        "dns timeout",
        "falhará com erro de conexão",
        "falhara com erro de conexao",
        "will fail with a connection",
        "não será executado com sucesso",
        "nao sera executado com sucesso",
        "will not be executed successfully",
        "não há como contornar",
        "nao ha como contornar",
        "no way to work around"
    ];

    private static readonly string[] SandboxSubjectMarkers =
    [
        "python_execute",
        "shell_execute",
        "node_execute",
        "sandbox",
        "aca",
        "azure container"
    ];

    public static bool HasSelfHostedSandbox(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Execution.Any(e =>
            string.Equals(e.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.SandboxEndpoint));

    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (!HasSelfHostedSandbox(runtimeConfig) || string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        // If python/shell actually failed with a real network error, the model may describe it.
        if (HasObservedSandboxNetworkFailure(steps))
            return false;

        var text = finalAnswer;
        var mentionsSandboxSubject = SandboxSubjectMarkers.Any(m =>
            text.Contains(m, StringComparison.OrdinalIgnoreCase));

        var inventsAca = AcaMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
                         || (mentionsSandboxSubject
                             && text.Contains("aca", StringComparison.OrdinalIgnoreCase)
                             && (text.Contains("isolad", StringComparison.OrdinalIgnoreCase)
                                 || text.Contains("isolated", StringComparison.OrdinalIgnoreCase)));

        var inventsNoNetwork = mentionsSandboxSubject
                               && NoNetworkMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

        // Hypothetical "if I tried it would fail" without any execution step.
        var inventsHypotheticalFailure =
            steps.Count == 0
            && mentionsSandboxSubject
            && (text.Contains("o que aconteceria", StringComparison.OrdinalIgnoreCase)
                || text.Contains("what would happen", StringComparison.OrdinalIgnoreCase)
                || text.Contains("se eu tentasse", StringComparison.OrdinalIgnoreCase)
                || text.Contains("if i tried", StringComparison.OrdinalIgnoreCase)
                || text.Contains("if i were to", StringComparison.OrdinalIgnoreCase));

        if (!inventsAca && !inventsNoNetwork && !inventsHypotheticalFailure)
            return false;

        feedback = BuildFeedback(runtimeConfig, inventsAca, inventsNoNetwork);
        return true;
    }

    private static bool HasObservedSandboxNetworkFailure(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Success)
                continue;

            if (!IsSandboxTool(step.ToolName))
                continue;

            var output = step.Output ?? string.Empty;
            if (output.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || output.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || output.Contains("NameResolution", StringComparison.OrdinalIgnoreCase)
                || output.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Failed to establish", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSandboxTool(string toolName) =>
        string.Equals(toolName, AgenticToolRegistry.PythonExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.ShellExecuteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AgenticToolRegistry.NodeExecuteToolName, StringComparison.OrdinalIgnoreCase);

    private static string BuildFeedback(AppRuntimeConfig config, bool inventsAca, bool inventsNoNetwork)
    {
        var reasons = new List<string>();
        if (inventsAca)
        {
            reasons.Add(TenantLocale.Select(
                config.DefaultLanguage,
                "this tenant uses self-hosted-sandbox, NOT Azure Container Apps",
                "este tenant usa self-hosted-sandbox, NÃO Azure Container Apps"));
        }

        if (inventsNoNetwork)
        {
            reasons.Add(TenantLocale.Select(
                config.DefaultLanguage,
                "outbound HTTP(S) from python_execute DOES work",
                "HTTP(S) externo a partir de python_execute FUNCIONA"));
        }

        var reasonText = string.Join("; ", reasons);
        return TenantLocale.Select(
            config.DefaultLanguage,
            "Rejected: you invented false sandbox limitations ("
            + reasonText
            + "). Do NOT claim ACA isolation or no network. "
            + "Call the real tools now: prefer configured MCP tools for Zuora (`server__tool`), "
            + "or `python_execute` for ad-hoc HTTP. Emit tool_calls — do not narrate hypothetical failures.",
            "Rejeitado: inventaste limitações falsas do sandbox ("
            + reasonText
            + "). NÃO digas que é ACA isolado nem que não há rede. "
            + "Chama as tools reais agora: prefere MCP configurado para Zuora (`servidor__tool`), "
            + "ou `python_execute` para HTTP ad-hoc. Emite tool_calls — não narres falhas hipotéticas.");
    }
}
