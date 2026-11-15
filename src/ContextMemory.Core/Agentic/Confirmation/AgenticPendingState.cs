using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticPendingKinds
{
    public const string Destructive = "destructive";
    public const string MaxIterations = "max-iterations";
}

public sealed class AgenticPendingState
{
    public required string PendingId { get; init; }
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public required string MatchedKeyword { get; init; }
    public required int Iteration { get; init; }
    public string Kind { get; init; } = AgenticPendingKinds.Destructive;
    public string DefaultLanguage { get; init; } = "en-US";
    public string? PartialAnswer { get; init; }
    public List<AgentExecutionStep> Steps { get; init; } = [];
    public List<OllamaMessage> Messages { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
