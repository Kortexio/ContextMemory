namespace ContextMemory.Core.Agentic.Policies;

/// <summary>Rejects calls that omit required schema fields (skips MCP open stubs).</summary>
public sealed class RequiredArgumentsToolCallPolicy : IAgenticToolCallPolicy
{
    public string Name => "required-arguments";

    public bool TryReject(AgenticToolCallPolicyContext context, out string feedback) =>
        AgenticRequiredArgumentsGuard.TryReject(
            context.ToolName,
            context.ArgumentsJson,
            context.TurnCatalog,
            context.RuntimeConfig,
            out feedback);
}
