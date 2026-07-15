using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class DestructiveActionMatch
{
    public required string Keyword { get; init; }
}

public static class AgenticDestructiveActionDetector
{
    public static DestructiveActionMatch? Analyze(
        OllamaToolCall toolCall,
        AgenticGuardrailsConfig guardrails)
    {
        foreach (var keyword in guardrails.RequireConfirmationFor)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (toolCall.Function.Arguments.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || toolCall.Function.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return new DestructiveActionMatch { Keyword = keyword.Trim() };
            }
        }

        return null;
    }
}
