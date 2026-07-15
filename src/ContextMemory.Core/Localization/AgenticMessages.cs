using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// Localized agentic loop, validation, and HITL strings.
/// </summary>
public static class AgenticMessages
{
    public static string ToolTimeout(AppRuntimeConfig config) =>
        TenantLocale.Select(config.DefaultLanguage, "Timeout during tool execution.", "Timeout durante execução de tool.");

    public static string LoopCompleted(int iterations, int toolCount, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Completed in {iterations} iteration(s) · {toolCount} tool(s).",
            $"Concluído em {iterations} iteração(ões) · {toolCount} tool(s).");

    public static string InvalidResponseRetry(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "The response is not valid. Fix it and try again.",
            "A resposta não é válida. Corrige e tenta novamente.");

    public static string MaxIterationsExceeded(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Could not complete the task within the configured iteration limit. ",
            "Não foi possível concluir a tarefa dentro do limite de iterações configurado. ");

    public static string ConfirmationReceived(string toolName, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Confirmation received for `{toolName}`.",
            $"Confirmação recebida para `{toolName}`.");

    public static string ValidationRejectedRetry(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Validation rejected — retrying…",
            "Validação rejeitada — nova tentativa…");

    public static string BuildConfirmationPrompt(AgenticPendingState pending, AppRuntimeConfig config)
    {
        if (string.Equals(pending.Kind, AgenticPendingKinds.MaxIterations, StringComparison.OrdinalIgnoreCase))
            return BuildMaxIterationsPrompt(pending, config);

        return TenantLocale.Select(
            config.DefaultLanguage,
            $"⚠️ **Human confirmation required** to execute `{pending.ToolName}` "
            + $"(action related to «{pending.MatchedKeyword}»).\n\n"
            + $"Arguments: `{pending.Arguments}`\n\n"
            + $"Reply **confirm** or send `[CONFIRM:{pending.PendingId}]` to authorize. "
            + $"Reply **cancel** to abort.",
            $"⚠️ **Confirmação humana necessária** para executar `{pending.ToolName}` "
            + $"(ação relacionada com «{pending.MatchedKeyword}»).\n\n"
            + $"Argumentos: `{pending.Arguments}`\n\n"
            + $"Responde **confirmo** ou envia `[CONFIRM:{pending.PendingId}]` para autorizar. "
            + $"Responde **cancelo** para abortar.");
    }

    private static string BuildMaxIterationsPrompt(AgenticPendingState pending, AppRuntimeConfig config)
    {
        var partial = string.IsNullOrWhiteSpace(pending.PartialAnswer)
            ? string.Empty
            : TenantLocale.Select(
                config.DefaultLanguage,
                $"**Proposed partial answer:**\n{pending.PartialAnswer}\n\n",
                $"**Resposta parcial proposta:**\n{pending.PartialAnswer}\n\n");

        return TenantLocale.Select(
            config.DefaultLanguage,
            $"⚠️ **Human review required** — the agent reached the iteration limit "
            + $"({pending.Iteration}) without finishing confidently.\n\n"
            + partial
            + $"Reply **approve** or `[CONFIRM:{pending.PendingId}]` to accept the partial answer. "
            + $"Reply **cancel** to reject.",
            $"⚠️ **Revisão humana necessária** — o agente atingiu o limite de iterações "
            + $"({pending.Iteration}) sem concluir com confiança.\n\n"
            + partial
            + $"Responde **aprovo** ou `[CONFIRM:{pending.PendingId}]` para aceitar a resposta parcial. "
            + $"Responde **cancelo** para rejeitar.");
    }

    public static string UserCancelledDestructive(string? language) =>
        TenantLocale.Select(
            language,
            "Action cancelled by the user. No destructive tool was executed.",
            "Ação cancelada pelo utilizador. Nenhuma tool destrutiva foi executada.");

    public static string HumanReviewApprovedDetail(string? language) =>
        TenantLocale.Select(
            language,
            "Human review approved — partial answer accepted.",
            "Revisão humana aprovada — resposta parcial aceite.");

    public static string PartialAnswerApproved(string? language) =>
        TenantLocale.Select(
            language,
            "Partial answer approved by the user.",
            "Resposta parcial aprovada pelo utilizador.");

    public static string TimeoutAfterIterations(int iterations, string? language) =>
        TenantLocale.Select(
            language,
            $"Timeout after {iterations} iteration(s).",
            $"Timeout após {iterations} iteração(ões).");

    public static string MaxIterationsReached(int maxIterations, string? language) =>
        TenantLocale.Select(
            language,
            $"Limit of {maxIterations} iterations reached.",
            $"Limite de {maxIterations} iterações atingido.");

    public static string MaxIterationsFallbackSuffix(string? language) =>
        TenantLocale.Select(
            language,
            "Please rephrase your request or contact support.",
            "Por favor, reformula o pedido ou contacta suporte.");

    public static string NetworkEgressBlocked(string target, string? language) =>
        TenantLocale.Select(
            language,
            $"Network egress blocked by tenant guardrail (networkEgress=restricted). "
            + $"Unauthorized destination: {target}. "
            + "Add the host to allowedEgressHosts or allowEgress on the tool.",
            $"Egress de rede bloqueado pelo guardrail do tenant (networkEgress=restricted). "
            + $"Destino não autorizado: {target}. "
            + "Adiciona o host a allowedEgressHosts ou allowEgress na tool.");

    public static string ProgressStarted(string? language) =>
        TenantLocale.Select(language, "Starting agentic loop…", "A iniciar loop agentic…");

    public static string ProgressLlmRequest(int iteration, string? language) =>
        TenantLocale.Select(
            language,
            $"Iteration {iteration} — querying model…",
            $"Iteração {iteration} — a consultar o modelo…");

    public static string ProgressToolStarted(string toolName, string? language) =>
        TenantLocale.Select(language, $"Running `{toolName}`…", $"A executar `{toolName}`…");

    public static string ProgressToolCompletedFallback(string toolName, string? language) =>
        TenantLocale.Select(language, $"Tool `{toolName}` completed.", $"Tool `{toolName}` concluída.");

    public static string ProgressValidating(string? language) =>
        TenantLocale.Select(language, "Validating final answer…", "A validar resposta final…");

    public static string ProgressValidationRejected(string? language) =>
        TenantLocale.Select(language, "Validation rejected — retrying…", "Validação rejeitada — nova tentativa…");

    public static string ProgressAwaitingConfirmation(string? language) =>
        TenantLocale.Select(
            language,
            "Awaiting human confirmation before executing the action.",
            "Aguarda confirmação humana antes de executar a ação.");

    public static string ProgressDestructiveBlocked(string? language) =>
        TenantLocale.Select(
            language,
            "Destructive action blocked until human confirmation.",
            "Ação destrutiva bloqueada até confirmação humana.");

    public static string ProgressHumanReviewAfterMaxIterations(string? language) =>
        TenantLocale.Select(
            language,
            "Human review required after iteration limit.",
            "Revisão humana necessária após limite de iterações.");

    public static string ProgressConfirmationReceived(string? language) =>
        TenantLocale.Select(
            language,
            "Confirmation received — executing pending action.",
            "Confirmação recebida — a executar ação pendente.");

    public static string ProgressCompleted(string? language) =>
        TenantLocale.Select(language, "Agentic loop completed.", "Loop agentic concluído.");

    public static string ProgressTimedOut(string? language) =>
        TenantLocale.Select(language, "Time limit reached — partial answer.", "Limite de tempo atingido — resposta parcial.");

    public static string ProgressMaxIterations(string? language) =>
        TenantLocale.Select(language, "Iteration limit reached.", "Limite de iterações atingido.");

    public static string ProgressTimedOutDetail(string? language) =>
        TenantLocale.Select(language, "Time limit reached.", "Limite de tempo atingido.");

    public static string ProgressMaxIterationsDetail(string? language) =>
        TenantLocale.Select(language, "Iteration limit reached.", "Limite de iterações atingido.");

    public static string ProgressCompletedDetail(string? language) =>
        TenantLocale.Select(language, "Completed.", "Concluído.");

    public static string ProgressCompletedWithStats(int iterations, int toolCount, string? language) =>
        TenantLocale.Select(
            language,
            $"Completed in {iterations} iteration(s) · {toolCount} tool(s).",
            $"Concluído em {iterations} iteração(ões) · {toolCount} tool(s).");

    public static string ProgressTimedOutWithStats(int iterations, string? language) =>
        TenantLocale.Select(
            language,
            $"Completed in {iterations} iteration(s) with partial answer.",
            $"Concluído em {iterations} iteração(ões) com resposta parcial.");

    public static string ToolStepFailed(int exitCode, string? language) =>
        TenantLocale.Select(language, $"failed (exit {exitCode})", $"falhou (exit {exitCode})");

    public static string PartialResponseSuffix(string? language) =>
        TenantLocale.Select(
            language,
            "_(Partial answer: the agentic loop time limit was reached before completion.)_",
            "_(Resposta parcial: o limite de tempo do loop agentic foi atingido antes da conclusão.)_");

    public static string TimeoutNoAnswer(string? language) =>
        TenantLocale.Select(
            language,
            "The agentic loop reached the configured time limit before producing an answer. "
            + "Please rephrase your request or try again.",
            "O loop agentic atingiu o limite de tempo configurado antes de produzir uma resposta. "
            + "Por favor, reformula o pedido ou tenta novamente.");

    public static string TimeoutProgressHeader(string? language) =>
        TenantLocale.Select(
            language,
            "The agentic loop reached the configured time limit. Progress so far:",
            "O loop agentic atingiu o limite de tempo configurado. Segue o progresso até ao momento:");

    public static string TimeoutStepLine(string toolName, int iteration, int exitCode, string? language) =>
        TenantLocale.Select(
            language,
            $"- **{toolName}** (iteration {iteration}, exit={exitCode})",
            $"- **{toolName}** (iteração {iteration}, exit={exitCode})");

    public static string TimeoutPartialFooter(string? language) =>
        TenantLocale.Select(
            language,
            "_Partial answer — the task was not completed within the available time._",
            "_Resposta parcial — a tarefa não foi concluída dentro do tempo disponível._");

    public static string JudgeDefaultReject(string? language) =>
        TenantLocale.Select(
            language,
            "The answer does not satisfy the user objective. Review and improve it.",
            "A resposta não satisfaz o objetivo do utilizador. Revisa e melhora.");

    public static string ExecutionLogStatus(AgentResult result, string? language)
    {
        if (result.Success)
            return TenantLocale.Select(language, "success", "sucesso");
        if (result.TimedOut)
            return TenantLocale.Select(language, "timeout-partial", "timeout-parcial");
        if (result.MaxIterationsReached)
            return TenantLocale.Select(language, "iteration-limit", "limite-iterações");
        return TenantLocale.Select(language, "partial", "parcial");
    }

    public static string ExecutionLogHeader(
        string timestamp,
        string status,
        int toolCount,
        int iterations,
        string? language) =>
        TenantLocale.Select(
            language,
            $"## [{timestamp}] agentic | {status} | {toolCount} tool(s) | {iterations} iteration(s)",
            $"## [{timestamp}] agentic | {status} | {toolCount} tool(s) | {iterations} iteração(ões)");

    public static string ExecutionLogObjectiveLabel(string? language) =>
        TenantLocale.Select(language, "**Objective:**", "**Objetivo:**");

    public static string ExecutionLogNoObjective(string? language) =>
        TenantLocale.Select(language, "(no objective)", "(sem objetivo)");

    public static string ExecutionLogStepsHeader(string? language) =>
        TenantLocale.Select(language, "### Steps executed", "### Passos executados");

    public static string ExecutionLogValidatedHeader(string? language) =>
        TenantLocale.Select(language, "### Validated result", "### Resultado validado");
}
