namespace ContextMemory.Core.Agentic.Policies;

/// <summary>
/// Blocks identical tool calls: wiki after any prior same-signature attempt;
/// other tools after an identical successful call.
/// </summary>
public sealed class DuplicateToolCallPolicy : IAgenticToolCallPolicy
{
    public string Name => "duplicate-tool-call";

    public bool TryReject(AgenticToolCallPolicyContext context, out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(context.ToolName) || context.Steps.Count == 0)
            return false;

        var name = ToolCallPolicyShared.NormalizeToolName(context.ToolName);
        var signature = ToolCallPolicyShared.BuildSignature(context.ToolName, context.ArgumentsJson);
        if (string.IsNullOrEmpty(signature))
            return false;

        var sameSignature = context.Steps.Where(s =>
            string.Equals(ToolCallPolicyShared.NormalizeToolName(s.ToolName), name, StringComparison.Ordinal)
            && string.Equals(
                ToolCallPolicyShared.BuildSignature(s.ToolName, s.Arguments),
                signature,
                StringComparison.Ordinal)).ToList();

        if (sameSignature.Count == 0)
            return false;

        if (ToolCallPolicyShared.IsQueryFocused(name))
        {
            feedback = BuildFeedback(
                context.ToolName,
                context.RuntimeConfig,
                afterFailure: sameSignature.All(s => !s.Success));
            return true;
        }

        if (!sameSignature.Any(s => s.Success))
            return false;

        feedback = BuildFeedback(context.ToolName, context.RuntimeConfig, afterFailure: false);
        return true;
    }

    private static string BuildFeedback(
        string toolName,
        Models.AppRuntimeConfig runtimeConfig,
        bool afterFailure)
    {
        var hasMcp = ToolCallPolicyShared.HasConfiguredMcp(runtimeConfig);
        var isWiki = ToolCallPolicyShared.IsQueryFocused(toolName);
        var prior = afterFailure
            ? ToolCallPolicyShared.Select(runtimeConfig, "already failed", "já falhou")
            : ToolCallPolicyShared.Select(runtimeConfig, "already succeeded", "já teve sucesso");

        if (hasMcp && isWiki)
        {
            return ToolCallPolicyShared.Select(
                runtimeConfig,
                $"Rejected: identical wiki_search {prior} — do NOT repeat the same query. "
                + "Either change the query substantially OR call a configured MCP tool now as ONLY JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(e.g. …__ask_zuora, …__query_objects; tool_describe if the schema is unclear).",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas a mesma query. "
                + "Ou muda a query de forma substancial OU chama agora uma tool MCP configurada como APENAS JSON "
                + "{\"tool\":\"server__tool\",\"arguments\":{...}} "
                + "(ex. …__ask_zuora, …__query_objects; tool_describe se o schema for unclear).");
        }

        if (hasMcp)
        {
            return ToolCallPolicyShared.Select(
                runtimeConfig,
                $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
                + "Try different arguments or another MCP/catalog tool as ONLY JSON "
                + "{\"tool\":\"name\",\"arguments\":{...}}.",
                $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
                + "Tenta arguments diferentes ou outra tool MCP/catálogo como APENAS JSON "
                + "{\"tool\":\"nome\",\"arguments\":{...}}.");
        }

        if (isWiki)
        {
            return ToolCallPolicyShared.Select(
                runtimeConfig,
                $"Rejected: identical wiki_search {prior} — do NOT repeat. "
                + "Change the query substantially or answer from the evidence you already have.",
                $"Rejeitado: wiki_search idêntica {prior} — NÃO repitas. "
                + "Muda a query de forma substancial ou responde com a evidência que já tens.");
        }

        return ToolCallPolicyShared.Select(
            runtimeConfig,
            $"Rejected: identical `{toolName}` {prior} — do NOT repeat the same arguments. "
            + "Change arguments or answer from existing evidence.",
            $"Rejeitado: `{toolName}` idêntica {prior} — NÃO repitas os mesmos arguments. "
            + "Muda os arguments ou responde com a evidência existente.");
    }
}
