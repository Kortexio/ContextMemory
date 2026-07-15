using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Runs the agentic tool loop with optional progress streaming.
/// </summary>
public interface IAgentOrchestrator
{
    Task<AgentResult> RunAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enrichedRequest,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgenticOrchestratorEvent> RunWithProgressAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest enrichedRequest,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);
}
