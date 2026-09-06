using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

public interface IExecutionPolicyEvaluator
{
    ExecutionPolicyDecision EvaluateTool(
        string toolName,
        string? arguments,
        ExecutionPolicy policy);
}
