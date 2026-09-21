using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Policies;

/// <summary>Soft-caps wiki_search/wiki_grep attempts per turn (real executions only).</summary>
public sealed class WikiBudgetToolCallPolicy : IAgenticToolCallPolicy
{
    public const int MaxWikiAttemptsPerTurn = 2;

    public string Name => "wiki-budget";

    public bool TryReject(AgenticToolCallPolicyContext context, out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(context.ToolName)
            || !ToolCallPolicyShared.IsQueryFocused(context.ToolName))
        {
            return false;
        }

        var wikiAttempts = context.Steps.Count(s =>
            !s.RejectedByGuard
            && ToolCallPolicyShared.IsQueryFocused(s.ToolName));
        if (wikiAttempts < MaxWikiAttemptsPerTurn)
            return false;

        var hasMcp = ToolCallPolicyShared.HasConfiguredMcp(context.RuntimeConfig);
        feedback = hasMcp
            ? ToolCallPolicyShared.Select(
                context.RuntimeConfig,
                "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
                + "Do NOT call wiki again. Call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
                + "NÃO chames wiki outra vez. Chama agora uma tool MCP como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).")
            : ToolCallPolicyShared.Select(
                context.RuntimeConfig,
                "Rejected: wiki_search/wiki_grep budget exhausted this turn. "
                + "Answer from evidence already gathered or change approach — do not call wiki again.",
                "Rejeitado: orçamento wiki_search/wiki_grep esgotado neste turno. "
                + "Responde com a evidência já recolhida ou muda de abordagem — não chames wiki outra vez.");
        return true;
    }
}
