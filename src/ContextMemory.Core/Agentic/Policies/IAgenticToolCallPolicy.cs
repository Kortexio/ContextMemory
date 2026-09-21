using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Policies;

/// <summary>Pre-execution policy that may reject a tool call with actionable feedback.</summary>
public interface IAgenticToolCallPolicy
{
    string Name { get; }

    bool TryReject(AgenticToolCallPolicyContext context, out string feedback);
}

public sealed record AgenticToolCallPolicyContext(
    string ToolName,
    string? ArgumentsJson,
    IReadOnlyList<AgentExecutionStep> Steps,
    AppRuntimeConfig RuntimeConfig,
    IReadOnlyList<OllamaTool>? TurnCatalog = null);

/// <summary>Runs registered policies in order; first rejection wins.</summary>
public sealed class AgenticToolCallPolicyChain
{
    private readonly IReadOnlyList<IAgenticToolCallPolicy> _policies;

    public AgenticToolCallPolicyChain(IEnumerable<IAgenticToolCallPolicy> policies) =>
        _policies = policies.ToList();

    public static AgenticToolCallPolicyChain CreateDefault() =>
        new([
            new WikiBudgetToolCallPolicy(),
            new WikiEmptyQueryToolCallPolicy(),
            new DuplicateToolCallPolicy(),
            new RequiredArgumentsToolCallPolicy()
        ]);

    public bool TryReject(AgenticToolCallPolicyContext context, out string feedback, out string? policyName)
    {
        foreach (var policy in _policies)
        {
            if (policy.TryReject(context, out feedback))
            {
                policyName = policy.Name;
                return true;
            }
        }

        feedback = string.Empty;
        policyName = null;
        return false;
    }
}
