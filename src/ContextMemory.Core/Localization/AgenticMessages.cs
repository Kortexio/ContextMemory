using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// Agentic loop, validation, and HITL strings (English only for model and harness context).
/// </summary>
public static class AgenticMessages
{
    public static string ToolTimeout(AppRuntimeConfig config) =>
        "Timeout during tool execution.";

    public static string LoopCompleted(int iterations, int toolCount, AppRuntimeConfig config) =>
        $"Completed in {iterations} iteration(s) · {toolCount} tool(s).";

    public static string InvalidResponseRetry(AppRuntimeConfig config) =>
        "The response is not valid. Fix it and try again.";

    public static string MaxIterationsExceeded(AppRuntimeConfig config) =>
        "Could not complete the task within the configured iteration limit. ";

    public static string ConfirmationReceived(string toolName, AppRuntimeConfig config) =>
        $"Confirmation received for `{toolName}`.";

    public static string ValidationRejectedRetry(AppRuntimeConfig config) =>
        "Validation rejected — retrying…";

    public static string BuildConfirmationPrompt(AgenticPendingState pending, AppRuntimeConfig config)
    {
        if (string.Equals(pending.Kind, AgenticPendingKinds.MaxIterations, StringComparison.OrdinalIgnoreCase))
            return BuildMaxIterationsPrompt(pending, config);

        return $"⚠️ **Human confirmation required** to execute `{pending.ToolName}` "
            + $"(action related to «{pending.MatchedKeyword}»).\n\n"
            + $"Arguments: `{pending.Arguments}`\n\n"
            + $"Reply **confirm** or send `[CONFIRM:{pending.PendingId}]` to authorize. "
            + $"Reply **cancel** to abort.";
    }

    private static string BuildMaxIterationsPrompt(AgenticPendingState pending, AppRuntimeConfig config)
    {
        var partial = string.IsNullOrWhiteSpace(pending.PartialAnswer)
            ? string.Empty
            : $"**Proposed partial answer:**\n{pending.PartialAnswer}\n\n";

        return $"⚠️ **Human review required** — the agent reached the iteration limit "
            + $"({pending.Iteration}) without finishing confidently.\n\n"
            + partial
            + $"Reply **approve** or `[CONFIRM:{pending.PendingId}]` to accept the partial answer. "
            + $"Reply **cancel** to reject.";
    }

    public static string UserCancelledDestructive(string? language) =>
        "Action cancelled by the user. No destructive tool was executed.";

    public static string HumanReviewApprovedDetail(string? language) =>
        "Human review approved — partial answer accepted.";

    public static string PartialAnswerApproved(string? language) =>
        "Partial answer approved by the user.";

    public static string TimeoutAfterIterations(int iterations, string? language) =>
        $"Timeout after {iterations} iteration(s).";

    public static string MaxIterationsReached(int maxIterations, string? language) =>
        $"Limit of {maxIterations} iterations reached.";

    public static string MaxIterationsFallbackSuffix(string? language) =>
        "Please rephrase your request or contact support.";

    public static string NetworkEgressBlocked(string target, string? language) =>
        $"Network egress blocked by tenant guardrail (networkEgress=restricted). "
        + $"Unauthorized destination: {target}. "
        + "Add the host to allowedEgressHosts or allowEgress on the tool.";

    public static string ProgressStarted(string? language) =>
        "Starting agentic loop…";

    public static string ProgressLlmRequest(int iteration, string? language) =>
        $"Iteration {iteration} — querying model…";

    public static string ProgressToolStarted(string toolName, string? language) =>
        $"Running `{toolName}`…";

    public static string ProgressToolCompletedFallback(string toolName, string? language) =>
        $"Tool `{toolName}` completed.";

    public static string ProgressValidating(string? language) =>
        "Validating final answer…";

    public static string ProgressValidationRejected(string? language) =>
        "Validation rejected — retrying…";

    public static string ProgressAwaitingConfirmation(string? language) =>
        "Awaiting human confirmation before executing the action.";

    public static string ProgressDestructiveBlocked(string? language) =>
        "Destructive action blocked until human confirmation.";

    public static string ProgressHumanReviewAfterMaxIterations(string? language) =>
        "Human review required after iteration limit.";

    public static string ProgressConfirmationReceived(string? language) =>
        "Confirmation received — executing pending action.";

    public static string ProgressCompleted(string? language) =>
        "Agentic loop completed.";

    public static string ProgressTimedOut(string? language) =>
        "Time limit reached — partial answer.";

    public static string ProgressMaxIterations(string? language) =>
        "Iteration limit reached.";

    public static string ProgressTimedOutDetail(string? language) =>
        "Time limit reached.";

    public static string ProgressMaxIterationsDetail(string? language) =>
        "Iteration limit reached.";

    public static string ProgressCompletedDetail(string? language) =>
        "Completed.";

    public static string ProgressCompletedWithStats(int iterations, int toolCount, string? language) =>
        $"Completed in {iterations} iteration(s) · {toolCount} tool(s).";

    public static string ProgressTimedOutWithStats(int iterations, string? language) =>
        $"Completed in {iterations} iteration(s) with partial answer.";

    public static string ToolStepFailed(int exitCode, string? language) =>
        $"failed (exit {exitCode})";

    public static string PartialResponseSuffix(string? language) =>
        "_(Partial answer: the agentic loop time limit was reached before completion.)_";

    public static string TimeoutNoAnswer(string? language) =>
        "The agentic loop reached the configured time limit before producing an answer. "
        + "Please rephrase your request or try again.";

    public static string ContextWindowExceeded(int promptTokens, int nCtx, string? language) =>
        $"The prompt ({promptTokens} tokens) exceeds the model context window (num_ctx={nCtx}). "
        + "Raise num_ctx in the Playground advanced settings or in the app LLM config "
        + "(for example 8192 or 32768) and retry.";

    public static string TimeoutProgressHeader(string? language) =>
        "The agentic loop reached the configured time limit. Progress so far:";

    public static string TimeoutStepLine(string toolName, int iteration, int exitCode, string? language) =>
        $"- **{toolName}** (iteration {iteration}, exit={exitCode})";

    public static string TimeoutPartialFooter(string? language) =>
        "_Partial answer — the task was not completed within the available time._";

    /// <summary>
    /// Short user-facing timeout message with unique tool counts (no per-step output dumps).
    /// </summary>
    public static string TimeoutShortSummary(string toolCountsSummary, string? language) =>
        "I could not finish within the configured time limit"
        + (string.IsNullOrWhiteSpace(toolCountsSummary)
            ? "."
            : $" (tools used: {toolCountsSummary}).")
        + " Please try again with a narrower question, or continue in a new turn.";

    public static string JudgeDefaultReject(string? language) =>
        "The answer does not satisfy the user objective. Review and improve it.";

    public static string ExecutionLogStatus(AgentResult result, string? language)
    {
        if (result.Success)
            return "success";
        if (result.TimedOut)
            return "timeout-partial";
        if (result.MaxIterationsReached)
            return "iteration-limit";
        return "partial";
    }

    public static string ExecutionLogHeader(
        string timestamp,
        string status,
        int toolCount,
        int iterations,
        string? language) =>
        $"## [{timestamp}] agentic | {status} | {toolCount} tool(s) | {iterations} iteration(s)";

    public static string ExecutionLogObjectiveLabel(string? language) =>
        "**Objective:**";

    public static string ExecutionLogNoObjective(string? language) =>
        "(no objective)";

    public static string ExecutionLogStepsHeader(string? language) =>
        "### Steps executed";

    public static string ExecutionLogValidatedHeader(string? language) =>
        "### Validated result";
}
