using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class DeterministicAgentValidator
{
    public Task<ValidationResult> ValidateAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var finalAnswer = request.FinalAnswer;
        var steps = request.Steps;
        var guardrails = request.RuntimeConfig.Agentic.Guardrails;
        var policy = request.RuntimeConfig.ResolvedPolicy;

        if (string.IsNullOrWhiteSpace(finalAnswer))
        {
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.EmptyFinalAnswer(request.RuntimeConfig)));
        }

        if (policy.HasKind(AgenticGuardrailKinds.SandboxClaim)
            && AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
                finalAnswer,
                steps,
                request.RuntimeConfig,
                out var sandboxFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.SandboxClaim)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.FabricatedSandboxLimitation(
                    configured ?? sandboxFeedback,
                    request.RuntimeConfig)));
        }

        if (policy.HasKind(AgenticGuardrailKinds.UrlFetch)
            && AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
                request.UserObjective,
                finalAnswer,
                steps,
                request.RuntimeConfig,
                out var urlFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.UrlFetch)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.UrlDescribedWithoutFetch(
                    configured ?? urlFeedback,
                    request.RuntimeConfig)));
        }

        if (policy.HasKind(AgenticGuardrailKinds.LiveDataEvidence)
            && AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
                request.UserObjective,
                finalAnswer,
                steps,
                request.RuntimeConfig,
                out var liveFeedback))
        {
            var configured = AgenticGuardrailConfigReader.GetFeedback(
                policy.FindByKind(AgenticGuardrailKinds.LiveDataEvidence)?.ConfigJson ?? "{}",
                request.RuntimeConfig.DefaultLanguage);
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.LiveDataWithoutEvidence(
                    configured ?? liveFeedback,
                    request.RuntimeConfig)));
        }

        if (guardrails.MinAnswerLength > 0 && finalAnswer.Trim().Length < guardrails.MinAnswerLength)
        {
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.TooShort(guardrails.MinAnswerLength, request.RuntimeConfig)));
        }

        var blockedPatterns = new List<string>(guardrails.BlockedAnswerPatterns);
        if (policy.HasKind(AgenticGuardrailKinds.BlockedPatterns))
        {
            var pack = policy.FindByKind(AgenticGuardrailKinds.BlockedPatterns);
            if (pack is not null)
                blockedPatterns.AddRange(AgenticGuardrailConfigReader.GetBlockedPatterns(pack.ConfigJson));
        }

        foreach (var pattern in blockedPatterns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (finalAnswer.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ValidationResult.Reject(
                    ValidationMessages.BlockedContent(pattern, request.RuntimeConfig)));
            }
        }

        // Discovery harness tools (tool_describe, skill_*, etc.) must not poison the turn:
        // a failed describe + successful MCP call would otherwise loop forever on RequireZeroExitCode.
        // Likewise, a failure that was later retried successfully (same tool, later step) must not
        // keep rejecting the final answer forever: once the model recovers, the earlier failure is
        // resolved and should no longer block validation.
        var failedSteps = steps
            .Where(s => !s.Success)
            .Where(s => !SessionDiscoveryTools.IsDiscoveryTool(s.ToolName))
            .Where(s => !HasLaterSuccessfulRetry(steps, s))
            .ToList();
        if (guardrails.RequireZeroExitCode && failedSteps.Count > 0)
        {
            var toolList = string.Join(", ", failedSteps.Select(s => s.ToolName).Distinct());
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.ToolsFailedExitCode(toolList, request.RuntimeConfig)));
        }

        if (policy.HasKind(AgenticGuardrailKinds.ToolFailureDisclosure)
            && failedSteps.Count > 0
            && !finalAnswer.Contains("erro", StringComparison.OrdinalIgnoreCase)
            && !finalAnswer.Contains("falhou", StringComparison.OrdinalIgnoreCase)
            && !finalAnswer.Contains("failed", StringComparison.OrdinalIgnoreCase)
            && !finalAnswer.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            var toolList = string.Join(", ", failedSteps.Select(s => s.ToolName).Distinct());
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.ToolsFailedNotMentioned(toolList, request.RuntimeConfig)));
        }

        foreach (var pattern in guardrails.ExpectedAnswerPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        finalAnswer,
                        pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                {
                    return Task.FromResult(ValidationResult.Reject(
                        ValidationMessages.PatternMismatch(pattern, request.RuntimeConfig)));
                }
            }
            catch
            {
                // ignore invalid regex configured by tenant
            }
        }

        foreach (var keyword in guardrails.RequireConfirmationFor)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            var destructiveStep = steps.FirstOrDefault(s =>
                s.Arguments.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || s.ToolName.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (destructiveStep is not null
                && !destructiveStep.Success
                && !finalAnswer.Contains("confirma", StringComparison.OrdinalIgnoreCase)
                && !finalAnswer.Contains("confirm", StringComparison.OrdinalIgnoreCase)
                && !finalAnswer.Contains("approval", StringComparison.OrdinalIgnoreCase)
                && !finalAnswer.Contains("aprovação", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ValidationResult.Reject(
                    ValidationMessages.ConfirmationRequired(keyword, request.RuntimeConfig)));
            }
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <summary>
    /// True when the same tool was called again after the given failed step and that later call
    /// succeeded, meaning the failure was retried and resolved within the same agent turn.
    /// </summary>
    private static bool HasLaterSuccessfulRetry(
        IReadOnlyList<AgentExecutionStep> steps,
        AgentExecutionStep failedStep)
    {
        var failedIndex = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            if (ReferenceEquals(steps[i], failedStep))
            {
                failedIndex = i;
                break;
            }
        }

        if (failedIndex < 0)
            return false;

        for (var i = failedIndex + 1; i < steps.Count; i++)
        {
            if (steps[i].Success
                && string.Equals(steps[i].ToolName, failedStep.ToolName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
