using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Mid-turn context compaction: archive transcript, summarize, shrink messages.
/// </summary>
public interface IAgentContextCompactor
{
    Task<ContextCompactionResult?> TryCompactAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        List<OllamaMessage> messages,
        int iteration,
        CancellationToken cancellationToken = default,
        WorkingMemory? workingMemory = null,
        int? tokenBudgetOverride = null,
        bool force = false);
}
