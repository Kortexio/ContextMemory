using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticSystemPromptBuilder
{
    public static string Build(
        AppRuntimeConfig runtimeConfig,
        string toolNamesSummary)
    {
        if (string.IsNullOrWhiteSpace(toolNamesSummary))
            return string.Empty;

        var profile = AgenticPromptProfileResolver.Resolve(runtimeConfig);
        var mcpServers = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var mcpLine = mcpServers.Count > 0
            ? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                $"\nMCP servers: {string.Join(", ", mcpServers)}.",
                $"\nServidores MCP: {string.Join(", ", mcpServers)}.")
            : string.Empty;

        return profile switch
        {
            AgenticPromptProfile.OpenAi => BuildOpenAi(toolNamesSummary, mcpLine),
            AgenticPromptProfile.Claude => BuildClaude(toolNamesSummary, mcpLine, runtimeConfig.DefaultLanguage),
            _ => BuildOllama(toolNamesSummary, mcpLine, runtimeConfig.DefaultLanguage)
        };
    }

    private static string BuildOllama(string toolNamesSummary, string mcpLine, string? language) =>
        TenantLocale.Select(
            language,
            $"""
            ## Agentic mode (Ollama/local)
            You have external tools: {toolNamesSummary}.{mcpLine}

            Tool calling rules:
            - Invoke tools only when needed: **external action** (commands, APIs) or **app documentation** via `wiki_search`.
            - Use `wiki_search` when the question needs facts from ingested docs (Jira, Confluence, SQL, etc.) that are not in session memory.
            - For greetings or questions answerable from session memory/context alone, respond **directly** without tools.
            - When you need a tool: emit **only** `tool_calls` with valid JSON arguments — no extra text.
            - After receiving results (`role=tool` messages), synthesize the final answer in natural language.
            - MCP tools use the `server__tool` format (e.g. `crm__get_customer`).
            - If a tool fails (exit code ≠ 0), explain the error clearly.
            - Never perform destructive actions without explicit user confirmation.
            """,
            $"""
            ## Modo agentic (Ollama/local)
            Tens ferramentas externas: {toolNamesSummary}.{mcpLine}

            Regras de tool calling:
            - Só invoca tools quando necessário: **ação externa** (comandos, APIs) ou **documentação da app** via `wiki_search`.
            - Usa `wiki_search` quando a pergunta precisar de factos de docs ingeridos (Jira, Confluence, SQL, etc.) que não estão na memória da sessão.
            - Para saudações ou perguntas respondíveis só com memória/contexto da sessão, responde **directamente** sem tools.
            - Quando precisares de uma tool: emite **apenas** `tool_calls` com argumentos JSON válidos — sem texto extra.
            - Depois de receberes resultados (mensagens `role=tool`), sintetiza a resposta final em linguagem natural.
            - Tools MCP usam o formato `servidor__tool` (ex: `crm__get_customer`).
            - Se uma tool falhar (exit code ≠ 0), explica o erro claramente.
            - Nunca executes acções destrutivas sem confirmação explícita do utilizador.
            """);

    private static string BuildOpenAi(string toolNamesSummary, string mcpLine) =>
        $"""
            ## Agent capabilities (OpenAI-compatible)
            Available functions: {toolNamesSummary}.{mcpLine}

            Function calling guidelines:
            - Prefer answering from session memory and context when sufficient — do not call functions unnecessarily.
            - Call a function only when external action or live data is required.
            - Provide complete, valid JSON in function arguments.
            - After receiving function results, respond with a clear, user-facing final answer.
            - MCP tools use the `server__tool` naming pattern.
            - If a function fails, explain what went wrong and suggest next steps.
            - Never perform destructive actions without explicit user confirmation.
            """;

    private static string BuildClaude(string toolNamesSummary, string mcpLine, string? language) =>
        TenantLocale.Select(
            language,
            $"""
            ## Agentic capabilities
            Available tools: {toolNamesSummary}.{mcpLine}

            Guidelines:
            - Before using a tool, check whether you can answer from context and memory already present.
            - Use tools only for external actions or data not in the session context.
            - Be economical: one well-chosen tool beats several redundant calls.
            - After tool results, present a complete, direct final answer.
            - MCP tools follow the `server__tool` pattern.
            - Communicate tool failures transparently.
            - Destructive actions require explicit user confirmation.
            """,
            $"""
            ## Capacidades agentic
            Ferramentas disponíveis: {toolNamesSummary}.{mcpLine}

            Orientações:
            - Antes de usar uma tool, avalia se consegues responder com o contexto e memória já presentes.
            - Usa tools apenas para acções externas ou dados que não estão no contexto da sessão.
            - Sê económico: uma tool bem escolhida é melhor que várias chamadas redundantes.
            - Após receber resultados de tools, apresenta uma resposta final completa e directa.
            - Tools MCP seguem o padrão `servidor__tool`.
            - Comunica falhas de tools de forma transparente.
            - Acções destrutivas exigem confirmação explícita do utilizador.
            """);
}
