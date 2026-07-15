using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Agentic;

public static class AgentPartialResponseFormatter
{
    public static string FormatTimeoutResponse(
        string? lastAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        string? language = null)
    {
        if (!string.IsNullOrWhiteSpace(lastAnswer))
        {
            return lastAnswer.TrimEnd()
                + "\n\n" + AgenticMessages.PartialResponseSuffix(language);
        }

        if (steps.Count == 0)
            return AgenticMessages.TimeoutNoAnswer(language);

        var lines = new List<string>
        {
            AgenticMessages.TimeoutProgressHeader(language),
            string.Empty
        };

        foreach (var step in steps)
        {
            lines.Add(AgenticMessages.TimeoutStepLine(
                step.ToolName,
                step.Iteration,
                step.ExitCode ?? 0,
                language));
            if (!string.IsNullOrWhiteSpace(step.Output))
                lines.Add($"  {Truncate(step.Output, 300)}");
        }

        lines.Add(string.Empty);
        lines.Add(AgenticMessages.TimeoutPartialFooter(language));
        return string.Join("\n", lines);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
