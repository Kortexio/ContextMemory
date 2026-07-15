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

        if (string.IsNullOrWhiteSpace(finalAnswer))
        {
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.EmptyFinalAnswer(request.RuntimeConfig)));
        }

        if (guardrails.MinAnswerLength > 0 && finalAnswer.Trim().Length < guardrails.MinAnswerLength)
        {
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.TooShort(guardrails.MinAnswerLength, request.RuntimeConfig)));
        }

        foreach (var pattern in guardrails.BlockedAnswerPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (finalAnswer.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ValidationResult.Reject(
                    ValidationMessages.BlockedContent(pattern, request.RuntimeConfig)));
            }
        }

        var failedSteps = steps.Where(s => !s.Success).ToList();
        if (guardrails.RequireZeroExitCode && failedSteps.Count > 0)
        {
            var toolList = string.Join(", ", failedSteps.Select(s => s.ToolName).Distinct());
            return Task.FromResult(ValidationResult.Reject(
                ValidationMessages.ToolsFailedExitCode(toolList, request.RuntimeConfig)));
        }

        if (failedSteps.Count > 0
            && !finalAnswer.Contains("erro", StringComparison.OrdinalIgnoreCase)
            && !finalAnswer.Contains("falhou", StringComparison.OrdinalIgnoreCase)
            && !finalAnswer.Contains("failed", StringComparison.OrdinalIgnoreCase))
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
}
