using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Validates agentic final answers against tenant guardrails.
/// </summary>
public interface IAgentValidator
{
    Task<ValidationResult> ValidateAsync(
        AgentValidationRequest request,
        CancellationToken cancellationToken = default);
}
