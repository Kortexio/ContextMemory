using ContextMemory.Core.Agentic;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Applies rate-limit cost for agentic turns.
/// </summary>
public interface IAgenticUsageCharger
{
    void TryCharge(
        string appId,
        AppRuntimeConfig runtimeConfig,
        AgentResult agentResult,
        ChatTurnContext turnContext);
}
