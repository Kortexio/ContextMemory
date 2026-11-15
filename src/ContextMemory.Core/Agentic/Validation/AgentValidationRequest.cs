using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgentValidationRequest
{
    public required string FinalAnswer { get; init; }
    public IReadOnlyList<AgentExecutionStep> Steps { get; init; } = [];
    public required AppRuntimeConfig RuntimeConfig { get; init; }
    public string? UserObjective { get; init; }
}

public enum AgentValidationMode
{
    Deterministic,
    LlmJudge,
    Hybrid
}

public static class AgentValidationModeParser
{
    public static AgentValidationMode Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "deterministic" or "deterministic-only" => AgentValidationMode.Deterministic,
            "llm-judge" or "llm" or "judge" => AgentValidationMode.LlmJudge,
            _ => AgentValidationMode.Hybrid
        };
}
