using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticToolDescriptionBuilder
{
    public static string BuildShellDescription(AppRuntimeConfig config)
    {
        var lang = config.DefaultLanguage;
        return TenantLocale.Select(
            lang,
            "Execute a shell command in an isolated Azure Container Apps session. "
            + "Use only when the user request requires running commands or inspecting the filesystem.",
            "Executa um comando shell num ambiente isolado (ACA). "
            + "Usa apenas quando for necessário executar comandos ou inspecionar ficheiros.");
    }

    public static string BuildPythonDescription(AppRuntimeConfig config)
    {
        var lang = config.DefaultLanguage;
        return TenantLocale.Select(
            lang,
            "Execute Python code in an isolated Azure Container Apps session.",
            "Executa código Python num ambiente isolado (ACA).");
    }

    public static string BuildNodeDescription(AppRuntimeConfig config)
    {
        var lang = config.DefaultLanguage;
        return TenantLocale.Select(
            lang,
            "Execute Node.js/JavaScript code in an isolated Azure Container Apps session.",
            "Executa código Node.js/JavaScript num ambiente isolado (ACA).");
    }

    public static string BuildContainerDescription(AppRuntimeConfig config, ExecutionToolConfig execution)
    {
        var image = string.IsNullOrWhiteSpace(execution.ContainerImage)
            ? "custom container"
            : execution.ContainerImage;
        var lang = config.DefaultLanguage;

        return TenantLocale.Select(
            lang,
            $"Execute a command in custom container '{image}' (Azure Container Apps Dynamic Session).",
            $"Executa um comando no container custom '{image}' (ACA Dynamic Session).");
    }

    public static string BuildMcpDescription(McpToolDefinition tool, AppRuntimeConfig config)
    {
        var baseDesc = string.IsNullOrWhiteSpace(tool.Description)
            ? tool.Name
            : tool.Description;
        var lang = config.DefaultLanguage;

        return TenantLocale.Select(
            lang,
            $"[MCP:{tool.ServerName}] {baseDesc}",
            $"[MCP:{tool.ServerName}] {baseDesc} (chamar como {tool.QualifiedName})");
    }

    public static string BuildWikiSearchDescription(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Search the app's global knowledge base (Jira, Confluence, SQL exports, and other ingested documents). Use when the question needs documented facts not present in session memory. Do not use for greetings or pure conversational replies.",
            "Pesquisa a base de conhecimento global da app (Jira, Confluence, exports SQL e outros documentos ingeridos). Usa quando a pergunta precisar de factos documentados que não estão na memória da sessão. Não uses para saudações ou conversa pura.");
}
