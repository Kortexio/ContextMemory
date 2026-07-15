using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Executes one agentic iteration: LLM call, tool dispatch, and validation.
/// </summary>
public interface IAgentLoopRunner
{
    Task<AgentResult> RunAsync(AgentLoopRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentLoopRequest
{
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public required OllamaRequest EnrichedRequest { get; init; }
    public required AppRuntimeConfig RuntimeConfig { get; init; }
    public required List<OllamaMessage> Messages { get; init; }
    public required List<AgentExecutionStep> Steps { get; init; }
    public required IReadOnlyList<OllamaTool> Tools { get; init; }
    public required IReadOnlyList<OllamaMcpServer> McpServers { get; init; }
    public int StartIteration { get; init; } = 1;
    public Action<AgenticProgressEvent>? Report { get; init; }
}
