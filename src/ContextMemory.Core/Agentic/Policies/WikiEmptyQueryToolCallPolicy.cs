namespace ContextMemory.Core.Agentic.Policies;

/// <summary>Rejects wiki_search/wiki_grep when query/pattern is missing or blank.</summary>
public sealed class WikiEmptyQueryToolCallPolicy : IAgenticToolCallPolicy
{
    public string Name => "wiki-empty-query";

    public bool TryReject(AgenticToolCallPolicyContext context, out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(context.ToolName)
            || !ToolCallPolicyShared.IsQueryFocused(context.ToolName))
        {
            return false;
        }

        var name = ToolCallPolicyShared.NormalizeToolName(context.ToolName);
        var query = ToolCallPolicyShared.ExtractQuery(context.ArgumentsJson);
        if (!string.IsNullOrWhiteSpace(query))
            return false;

        var hasMcp = ToolCallPolicyShared.HasConfiguredMcp(context.RuntimeConfig);
        var field = string.Equals(name, "wiki_grep", StringComparison.Ordinal) ? "pattern" : "query";

        feedback = hasMcp
            ? ToolCallPolicyShared.Select(
                context.RuntimeConfig,
                $"Rejected: `{name}` needs a non-empty \"{field}\". "
                + $"Retry as ONLY JSON {{\"tool\":\"{name}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}} "
                + "OR call a configured MCP tool now "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects).",
                $"Rejeitado: `{name}` precisa de \"{field}\" não vazio. "
                + $"Repete como APENAS JSON {{\"tool\":\"{name}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}} "
                + "OU chama agora uma tool MCP "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects).")
            : ToolCallPolicyShared.Select(
                context.RuntimeConfig,
                $"Rejected: `{name}` needs a non-empty \"{field}\". "
                + $"Retry as ONLY JSON {{\"tool\":\"{name}\",\"arguments\":{{\"{field}\":\"concrete keywords\"}}}}.",
                $"Rejeitado: `{name}` precisa de \"{field}\" não vazio. "
                + $"Repete como APENAS JSON {{\"tool\":\"{name}\",\"arguments\":{{\"{field}\":\"palavras concretas\"}}}}.");
        return true;
    }
}
