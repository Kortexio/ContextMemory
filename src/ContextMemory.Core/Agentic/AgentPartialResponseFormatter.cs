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

        var counts = steps
            .GroupBy(s => s.ToolName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        var summary = string.Join(", ", counts);
        return AgenticMessages.TimeoutShortSummary(summary, language);
    }
}
