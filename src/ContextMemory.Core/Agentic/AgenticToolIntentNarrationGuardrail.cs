using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Enforcement for guardrail kind <see cref="AgenticGuardrailKinds.ToolSurfaceHidden"/>:
/// keeps tool mechanics off the user-facing final answer.
/// <list type="bullet">
/// <item>Rejects answers that name internal tools (wiki_search, …) — the end user must not see them.</item>
/// <item>Rejects answers that only announce intent / ask permission instead of emitting tool_calls.</item>
/// </list>
/// Harness (Weak) still helps models emit tool_calls; this guardrail validates the visible answer.
/// </summary>
public static class AgenticToolIntentNarrationGuardrail
{
    private static readonly string[] ToolNameMarkers =
    [
        "wiki_search",
        "wiki_grep",
        "wiki_get",
        "wiki_read",
        "fetch_url",
        "http_request",
        "web_search",
        "query_objects",
        "python_execute",
        "shell_execute",
        "node_execute",
        "browser_navigate",
        "browser_snapshot",
        "browser_click",
        "browser_type",
        "browser_screenshot",
        "read_image",
        "parse_pdf",
        "canvas_write",
        "canvas_read",
        "todo_write",
        "tool_search",
        "tool_describe",
        "skill_search",
        "skill_read",
        "rule_search",
        "rule_read",
        "tool_calls",
        "tool call",
        "tool_call"
    ];

    private static readonly string[] IntentPhrases =
    [
        "vou usar",
        "vou buscar",
        "vou procurar",
        "vou chamar",
        "vou consultar",
        "usando a ferramenta",
        "usando a tool",
        "usando as tools",
        "usando as ferramentas",
        "posso usar",
        "posso chamar",
        "posso invocar",
        "deixa-me usar",
        "deixe-me usar",
        "permites que use",
        "permite que use",
        "irei usar",
        "irei buscar",
        "i'll use",
        "i will use",
        "i am going to use",
        "i'm going to use",
        "let me use",
        "may i use",
        "can i use",
        "should i use",
        "going to use",
        "i'll call",
        "i will call",
        "i'll search",
        "i will search",
        "i'll look up",
        "using the tool",
        "using tools",
        "allow me to use",
        "would you like me to use"
    ];

    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        IReadOnlyList<AgentExecutionStep> steps,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        if (!runtimeConfig.Agentic.Enabled)
            return false;

        var lang = runtimeConfig.DefaultLanguage;
        var hasMcp = HasConfiguredMcp(runtimeConfig);
        var hasNonDiscovery = HasSuccessfulNonDiscoveryTool(steps);
        var hasDiscovery = HasSuccessfulDiscoveryTool(steps);

        // Always: never leak internal tool names into the user-visible answer.
        if (ContainsToolName(finalAnswer))
        {
            feedback = BuildLeakFeedback(lang, hasMcp, hasNonDiscovery, hasDiscovery);
            return true;
        }

        if (hasNonDiscovery)
            return false;

        if (!LooksLikeToolIntentPhrase(finalAnswer))
            return false;

        feedback = BuildIntentFeedback(lang, hasMcp, hasDiscovery);
        return true;
    }

    public static bool LooksLikeToolIntent(string finalAnswer) =>
        ContainsToolName(finalAnswer) || LooksLikeToolIntentPhrase(finalAnswer);

    public static bool ContainsToolName(string finalAnswer)
    {
        foreach (var tool in ToolNameMarkers)
        {
            if (finalAnswer.Contains(tool, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildLeakFeedback(
        string? lang,
        bool hasMcp,
        bool hasNonDiscovery,
        bool hasDiscovery)
    {
        if (hasNonDiscovery)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: the final answer names internal tools. Rewrite for the end user without tool names, "
                + "APIs, or mechanics — deliver the result only.",
                "Rejeitado: a resposta final nomeia tools internas. Reescreve para o utilizador final sem nomes de tools, "
                + "APIs ou mecânica — entrega só o resultado.");
        }

        return BuildIntentFeedback(lang, hasMcp, hasDiscovery);
    }

    private static string BuildIntentFeedback(string? lang, bool hasMcp, bool hasDiscovery)
    {
        if (hasMcp && !hasDiscovery)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: do not narrate or name tools. Your entire next message must be ONLY this JSON "
                + "(no prose): {\"tool\":\"tool_search\",\"arguments\":{\"query\":\"account\"}} "
                + "Then describe and call the matched MCP tool the same way. After evidence arrives, answer with results only.",
                "Rejeitado: não narres nem nomes tools. A tua próxima mensagem tem de ser APENAS este JSON "
                + "(sem prosa): {\"tool\":\"tool_search\",\"arguments\":{\"query\":\"account\"}} "
                + "Depois descreve e chama a tool MCP correspondente da mesma forma. Com evidência, responde só com o resultado.");
        }

        if (hasMcp && hasDiscovery)
        {
            return TenantLocale.Select(
                lang,
                "Rejected: do not narrate tools. Emit the next call as ONLY JSON "
                + "{\"tool\":\"exact_qualified_name\",\"arguments\":{...}} "
                + "(use a name from the prior discovery result; call tool_describe first if the schema is unknown). "
                + "After the MCP result, answer the user without naming tools.",
                "Rejeitado: não narres tools. Emite a próxima chamada como APENAS JSON "
                + "{\"tool\":\"nome_qualificado_exacto\",\"arguments\":{...}} "
                + "(usa um nome do resultado de discovery; tool_describe se o schema for desconhecido). "
                + "Depois do resultado MCP, responde ao utilizador sem nomear tools.");
        }

        return TenantLocale.Select(
            lang,
            "Rejected: you narrated an intent to use tools (or asked permission) instead of calling them. "
            + "Emit tool_calls now with valid JSON — do not ask the user, do not announce. "
            + "After results arrive, answer in natural language without naming tools.",
            "Rejeitado: narraste a intenção de usar tools (ou pediste permissão) em vez de as chamares. "
            + "Emite tool_calls agora com JSON válido — não perguntes ao utilizador, não anuncies. "
            + "Depois dos resultados, responde em linguagem natural sem nomear tools.");
    }

    private static bool LooksLikeToolIntentPhrase(string finalAnswer)
    {
        var text = finalAnswer.ToLowerInvariant();
        foreach (var phrase in IntentPhrases)
        {
            if (text.Contains(phrase, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasConfiguredMcp(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && i.IsConfigured);

    private static bool HasSuccessfulDiscoveryTool(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Success && SessionDiscoveryTools.IsDiscoveryTool(step.ToolName))
                return true;
        }

        return false;
    }

    private static bool HasSuccessfulNonDiscoveryTool(IReadOnlyList<AgentExecutionStep> steps)
    {
        foreach (var step in steps)
        {
            if (!step.Success)
                continue;
            if (SessionDiscoveryTools.IsDiscoveryTool(step.ToolName))
                continue;
            return true;
        }

        return false;
    }
}
