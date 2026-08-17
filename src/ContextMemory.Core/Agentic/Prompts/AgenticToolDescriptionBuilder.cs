using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticToolDescriptionBuilder
{
    public static string BuildShellDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        var lang = config.DefaultLanguage;
        if (IsSelfHosted(execution))
        {
            return TenantLocale.Select(
                lang,
                "Execute a shell command in the self-hosted sandbox. "
                + "Outbound HTTP(S) is allowed. Preinstalled CLIs include git and Azure CLI (az). "
                + "Prefer MCP tools (azure-monitor__*, github) when configured; use az/git as fallback. "
                + "Working directory is ephemeral (deleted after the call). "
                + "Print results to stdout — do not assume files persist. Never echo secrets.",
                "Executa um comando shell no sandbox self-hosted. "
                + "Tem acesso a HTTP(S) externo. CLIs pré-instalados: git e Azure CLI (az). "
                + "Prefere tools MCP (azure-monitor__*, github) quando existirem; usa az/git como fallback. "
                + "O diretório de trabalho é efémero (apagado após a chamada). "
                + "Imprime resultados em stdout — não assumes que ficheiros persistem. Nunca echoes secrets.");
        }

        return TenantLocale.Select(
            lang,
            "Execute a shell command in an isolated Azure Container Apps session. "
            + "Network access depends on the ACA pool configuration. "
            + "Use only when the user request requires running commands or inspecting the filesystem.",
            "Executa um comando shell num ambiente isolado (ACA). "
            + "Acesso de rede depende da configuração do pool ACA. "
            + "Usa apenas quando for necessário executar comandos ou inspecionar ficheiros.");
    }

    public static string BuildPythonDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        var lang = config.DefaultLanguage;
        if (IsSelfHosted(execution))
        {
            return TenantLocale.Select(
                lang,
                "Execute Python code in the self-hosted sandbox. "
                + "Outbound HTTP(S) works. Packages: requests, httpx, beautifulsoup4, lxml, html2text, "
                + "pyyaml, python-dateutil, openpyxl, PyJWT, ddgs, playwright (Chromium). "
                + "Use ddgs / httpx+BeautifulSoup for search+fetch+parse; Playwright for JS-heavy pages. "
                + "Working files are ephemeral — print results to stdout. "
                + "For Zuora and other configured MCP APIs, prefer MCP tools (`server__tool`) instead of hand-rolled OAuth in Python. "
                + "For general web search, the gateway web-search tools may already be available — prefer those when present.",
                "Executa código Python no sandbox self-hosted. "
                + "HTTP(S) funciona. Pacotes: requests, httpx, beautifulsoup4, lxml, html2text, "
                + "pyyaml, python-dateutil, openpyxl, PyJWT, ddgs, playwright (Chromium). "
                + "Usa ddgs / httpx+BeautifulSoup para search+fetch+parse; Playwright para páginas com JS. "
                + "Ficheiros são efémeros — imprime resultados em stdout. "
                + "Para Zuora e outras APIs com MCP, prefere tools MCP (`servidor__tool`) em vez de OAuth manual em Python. "
                + "Para busca web geral, as tools de web-search do gateway podem já existir — prefere-as quando disponíveis.");
        }

        return TenantLocale.Select(
            lang,
            "Execute Python code in an isolated Azure Container Apps session. "
            + "Network egress may be blocked depending on the ACA pool — prefer MCP tools for external APIs when unsure.",
            "Executa código Python num ambiente isolado (ACA). "
            + "A rede pode estar bloqueada consoante o pool ACA — prefere tools MCP para APIs externas se não tiveres a certeza.");
    }

    public static string BuildNodeDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        var lang = config.DefaultLanguage;
        if (IsSelfHosted(execution))
        {
            return TenantLocale.Select(
                lang,
                "Execute Node.js/JavaScript code in the self-hosted sandbox. "
                + "Outbound HTTP(S) is allowed. Working directory is ephemeral.",
                "Executa código Node.js/JavaScript no sandbox self-hosted. "
                + "Tem acesso HTTP(S) externo. O diretório de trabalho é efémero.");
        }

        return TenantLocale.Select(
            lang,
            "Execute Node.js/JavaScript code in an isolated Azure Container Apps session. "
            + "Network egress may be blocked depending on the ACA pool.",
            "Executa código Node.js/JavaScript num ambiente isolado (ACA). "
            + "A rede pode estar bloqueada consoante o pool ACA.");
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
            "Search the app's global knowledge base (Jira, Confluence, SQL exports, and other ingested documents). Use when the question needs documented facts not present in session memory. Acronyms resolve from digests and the auto-refreshed wiki glossary (e.g. VVE ↔ Virtual Vehicle Enablement). Optional asOf (ISO-8601) retrieves what was valid at that time. Do not use for greetings or pure conversational replies.",
            "Pesquisa a base de conhecimento global da app (Jira, Confluence, exports SQL e outros documentos ingeridos). Usa quando a pergunta precisar de factos documentados que não estão na memória da sessão. Siglas resolvem-se a partir dos digests e do wiki:glossary auto-atualizado (ex. VVE ↔ Virtual Vehicle Enablement). asOf opcional (ISO-8601) devolve o que era válido nessa data. Não uses para saudações ou conversa pura.");

    private static bool IsSelfHosted(ExecutionToolConfig? execution) =>
        execution is not null
        && string.Equals(execution.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase);
}
