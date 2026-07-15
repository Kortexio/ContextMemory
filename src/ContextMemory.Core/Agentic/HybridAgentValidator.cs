using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class HybridAgentValidator : IAgentValidator
{
    private readonly DeterministicAgentValidator _deterministic;
    private readonly LlmJudgeAgentValidator _llmJudge;
    private readonly ILogger<HybridAgentValidator> _logger;

    public HybridAgentValidator(
        DeterministicAgentValidator deterministic,
        LlmJudgeAgentValidator llmJudge,
        ILogger<HybridAgentValidator> logger)
    {
        _deterministic = deterministic;
        _llmJudge = llmJudge;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = AgentValidationModeParser.Parse(request.RuntimeConfig.Agentic.Guardrails.ValidationMode);

        if (mode is AgentValidationMode.LlmJudge)
        {
            var basic = await RunBasicSafetyChecksAsync(request).ConfigureAwait(false);
            if (!basic.IsValid)
                return basic;

            return await _llmJudge.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var deterministic = await _deterministic.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!deterministic.IsValid)
            return deterministic;

        if (mode is AgentValidationMode.Deterministic)
            return deterministic;

        _logger.LogDebug(
            "Running LLM-as-judge for app {AppId} after deterministic validation passed",
            request.RuntimeConfig.AppId);

        return await _llmJudge.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static Task<ValidationResult> RunBasicSafetyChecksAsync(AgentValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FinalAnswer))
        {
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.EmptyFinalAnswer(request.RuntimeConfig)));
        }

        return Task.FromResult(ValidationResult.Ok());
    }
}
