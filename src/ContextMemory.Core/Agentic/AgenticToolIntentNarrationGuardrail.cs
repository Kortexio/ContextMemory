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

        // Always: never leak internal tool names into the user-visible answer.
        if (ContainsToolName(finalAnswer))
        {
            feedback = TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "Rejected: the final answer names internal tools. Rewrite for the end user without tool names, "
                + "APIs, or mechanics — deliver the result only. If you still need data, emit tool_calls silently "
                + "(do not announce them).",
                "Rejeitado: a resposta final nomeia tools internas. Reescreve para o utilizador final sem nomes de tools, "
                + "APIs ou mecânica — entrega só o resultado. Se ainda precisares de dados, emite tool_calls em silêncio "
                + "(não as anuncies).");
            return true;
        }

        if (HasSuccessfulNonDiscoveryTool(steps))
            return false;

        if (!LooksLikeToolIntentPhrase(finalAnswer))
            return false;

        feedback = TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Rejected: you narrated an intent to use tools (or asked permission) instead of calling them. "
            + "Emit tool_calls now with valid JSON — do not ask the user, do not announce. "
            + "After results arrive, answer in natural language without naming tools.",
            "Rejeitado: narraste a intenção de usar tools (ou pediste permissão) em vez de as chamares. "
            + "Emite tool_calls agora com JSON válido — não perguntes ao utilizador, não anuncies. "
            + "Depois dos resultados, responde em linguagem natural sem nomear tools.");
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
