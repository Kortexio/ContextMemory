using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Agentic;

public static class AgenticProgressFormatter
{
    public static string FormatEvent(AgenticProgressEvent evt, string? language = null) =>
        evt.Phase switch
        {
            AgenticProgressPhase.Started => AgenticMessages.ProgressStarted(language),
            AgenticProgressPhase.LlmRequest =>
                AgenticMessages.ProgressLlmRequest(evt.Iteration ?? 1, language),
            AgenticProgressPhase.ToolStarted =>
                AgenticMessages.ProgressToolStarted(evt.ToolName ?? "tool", language),
            AgenticProgressPhase.ToolCompleted when evt.Step is not null =>
                FormatToolCompleted(evt.Step, language),
            AgenticProgressPhase.ToolCompleted =>
                AgenticMessages.ProgressToolCompletedFallback(evt.ToolName ?? "tool", language),
            AgenticProgressPhase.Validating => AgenticMessages.ProgressValidating(language),
            AgenticProgressPhase.ValidationRejected =>
                evt.Detail ?? AgenticMessages.ProgressValidationRejected(language),
            AgenticProgressPhase.AwaitingConfirmation =>
                evt.Detail ?? AgenticMessages.ProgressAwaitingConfirmation(language),
            AgenticProgressPhase.ConfirmationReceived =>
                evt.Detail ?? AgenticMessages.ProgressConfirmationReceived(language),
            AgenticProgressPhase.Completed =>
                evt.Detail ?? AgenticMessages.ProgressCompleted(language),
            AgenticProgressPhase.TimedOut =>
                evt.Detail ?? AgenticMessages.ProgressTimedOut(language),
            AgenticProgressPhase.MaxIterations =>
                evt.Detail ?? AgenticMessages.ProgressMaxIterations(language),
            _ => evt.Detail ?? evt.Phase.ToString()
        };

    public static string FormatToolCompleted(AgentExecutionStep step, string? language = null)
    {
        var status = step.Success ? "OK" : AgenticMessages.ToolStepFailed(step.ExitCode ?? -1, language);
        var duration = step.Duration.TotalMilliseconds >= 1
            ? $" · {step.Duration.TotalMilliseconds:F0} ms"
            : string.Empty;
        return $"`{step.ToolName}` {status}{duration}";
    }

    public static string FormatStepsTimeline(IReadOnlyList<AgentExecutionStep> steps, string? language = null)
    {
        if (steps.Count == 0)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            steps.Select((s, i) => $"{i + 1}. {FormatToolCompleted(s, language)}"));
    }
}
