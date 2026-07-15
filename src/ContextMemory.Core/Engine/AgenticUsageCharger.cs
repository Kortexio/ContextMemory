using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Engine;

public sealed class AgenticUsageCharger : IAgenticUsageCharger
{
    private readonly IRateLimitService _rateLimitService;

    public AgenticUsageCharger(IRateLimitService rateLimitService) =>
        _rateLimitService = rateLimitService;

    public void TryCharge(
        string appId,
        AppRuntimeConfig runtimeConfig,
        AgentResult agentResult,
        ChatTurnContext turnContext)
    {
        if (turnContext.AgenticUsageCharged || !runtimeConfig.AgenticEnabled)
            return;

        var extraIterations = Math.Max(0, agentResult.Iterations - 1);
        var extraTokens = extraIterations * Math.Max(0, runtimeConfig.RateLimits.AgenticTokensPerIteration);
        var extraToolCalls = Math.Max(0, agentResult.Steps.Count - 1);

        if (extraTokens > 0 || extraToolCalls > 0)
            _rateLimitService.ChargeAdditional(appId, extraTokens, extraToolCalls);

        turnContext.AgenticUsageCharged = true;
    }
}
