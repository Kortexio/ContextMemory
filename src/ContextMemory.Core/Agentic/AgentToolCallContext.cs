using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>Bundles the many ProcessAsync parameters into one context object.</summary>
public sealed class AgentToolCallContext
{
    public required OllamaToolCall ToolCall { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public required AppRuntimeConfig RuntimeConfig { get; init; }
    public required int Iteration { get; init; }
    public required List<AgentExecutionStep> Steps { get; init; }
    public required List<OllamaMessage> Messages { get; init; }
    public Action<AgenticProgressEvent>? Report { get; init; }
    public bool SkipConfirmation { get; init; }
    public IReadOnlyList<OllamaTool>? TurnCatalog { get; init; }
}
