using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Agentic;

public sealed class HybridAgentValidator : IAgentValidator
{
    private readonly DeterministicAgentValidator _deterministic;
    private readonly LlmJudgeAgentValidator _llmJudge;
    private readonly IAgenticUrlAvailabilityChecker? _urlChecker;
    private readonly ILogger<HybridAgentValidator> _logger;

    public HybridAgentValidator(
        DeterministicAgentValidator deterministic,
        LlmJudgeAgentValidator llmJudge,
        ILogger<HybridAgentValidator> logger,
        IAgenticUrlAvailabilityChecker? urlChecker = null)
    {
        _deterministic = deterministic;
        _llmJudge = llmJudge;
        _logger = logger;
        _urlChecker = urlChecker;
    }

    public async Task<ValidationResult> ValidateAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = AgentValidationModeParser.Parse(request.RuntimeConfig.Agentic.Guardrails.ValidationMode);

        if (mode is AgentValidationMode.LlmJudge)
        {
            var basic = await RunBasicSafetyChecksAsync(request, cancellationToken).ConfigureAwait(false);
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

    private async Task<ValidationResult> RunBasicSafetyChecksAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FinalAnswer))
        {
            return ValidationResult.Reject(
                ValidationMessages.EmptyFinalAnswer(request.RuntimeConfig));
        }

        var policy = request.RuntimeConfig.ResolvedPolicy;

        if (policy.HasKind(AgenticGuardrailKinds.SandboxClaim)
            && AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
                request.FinalAnswer,
                request.Steps,
                request.RuntimeConfig,
                out var sandboxFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.SandboxClaim)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return ValidationResult.Reject(
                ValidationMessages.FabricatedSandboxLimitation(
                    configured ?? sandboxFeedback,
                    request.RuntimeConfig));
        }

        if (policy.HasKind(AgenticGuardrailKinds.UrlFetch)
            && AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
                request.UserObjective,
                request.FinalAnswer,
                request.Steps,
                request.RuntimeConfig,
                out var urlFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.UrlFetch)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return ValidationResult.Reject(
                ValidationMessages.UrlDescribedWithoutFetch(
                    configured ?? urlFeedback,
                    request.RuntimeConfig));
        }

        if (policy.HasKind(AgenticGuardrailKinds.LiveDataEvidence)
            && AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
                request.UserObjective,
                request.FinalAnswer,
                request.Steps,
                request.RuntimeConfig,
                out var liveFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.LiveDataEvidence)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return ValidationResult.Reject(
                ValidationMessages.LiveDataWithoutEvidence(
                    configured ?? liveFeedback,
                    request.RuntimeConfig));
        }

        if (policy.HasKind(AgenticGuardrailKinds.ToolSurfaceHidden)
            && AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
                request.FinalAnswer,
                request.Steps,
                request.RuntimeConfig,
                out var toolIntentFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.ToolSurfaceHidden)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return ValidationResult.Reject(
                ValidationMessages.ToolIntentNarration(
                    configured ?? toolIntentFeedback,
                    request.RuntimeConfig));
        }

        var extended = await AgenticExtendedGuardrailRunner.TryGetRejectionAsync(
                request, _urlChecker, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(extended))
            return ValidationResult.Reject(extended);

        return ValidationResult.Ok();
    }
}
