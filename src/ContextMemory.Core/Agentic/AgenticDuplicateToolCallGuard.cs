using ContextMemory.Core.Agentic.Policies;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Facade over the wiki/duplicate policies for callers and tests that still target this type.
/// Prefer <see cref="AgenticToolCallPolicyChain"/> for new code.
/// </summary>
public static class AgenticDuplicateToolCallGuard
{
    /// <summary>Max wiki_search/wiki_grep attempts (success or fail) before forcing a pivot.</summary>
    public const int MaxWikiAttemptsPerTurn = WikiBudgetToolCallPolicy.MaxWikiAttemptsPerTurn;

    private static readonly AgenticToolCallPolicyChain WikiAndDuplicateChain = new([
        new WikiBudgetToolCallPolicy(),
        new WikiEmptyQueryToolCallPolicy(),
        new DuplicateToolCallPolicy()
    ]);

    public static bool TryReject(
        string toolName,
        string? argumentsJson,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback) =>
        WikiAndDuplicateChain.TryReject(
            new AgenticToolCallPolicyContext(toolName, argumentsJson, steps, runtimeConfig),
            out feedback,
            out _);

    public static string BuildSignature(string toolName, string? argumentsJson) =>
        ToolCallPolicyShared.BuildSignature(toolName, argumentsJson);

    internal static string NormalizeText(string? value) =>
        ToolCallPolicyShared.NormalizeText(value);
}
