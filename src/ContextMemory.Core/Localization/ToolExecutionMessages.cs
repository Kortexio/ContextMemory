using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// Localized tool execution and agentic loop messages returned to the LLM.
/// </summary>
public static class ToolExecutionMessages
{
    public static string ToolNotRegistered(string toolName, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Tool '{toolName}' is not registered for this tenant.",
            $"Tool '{toolName}' não está registada para este tenant.");

    public static string ToolNotRegisteredSelfHosted(string toolName, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Tool '{toolName}' is not registered for this tenant (self-hosted-sandbox).",
            $"Tool '{toolName}' não está registada para este tenant (self-hosted-sandbox).");

    public static string AcaPoolNotConfigured(string runtime, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Error: no ACA poolEndpoint configured for runtime '{runtime}'.",
            $"Erro: nenhum poolEndpoint ACA configurado para runtime '{runtime}'.");

    public static string SandboxEndpointNotConfigured(string runtime, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Error: no sandboxEndpoint configured for runtime '{runtime}'.",
            $"Erro: nenhum sandboxEndpoint configurado para runtime '{runtime}'.");

    public static string UnsupportedAcaRuntime(string runtime, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"ACA runtime '{runtime}' is not supported.",
            $"Runtime ACA '{runtime}' não suportado.");

    public static string UnsupportedSelfHostedRuntime(string runtime, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Self-hosted runtime '{runtime}' is not supported.",
            $"Runtime self-hosted '{runtime}' não suportado.");

    public static string MissingCommandParameter(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Error: missing or invalid 'command' parameter.",
            "Erro: parâmetro 'command' em falta ou inválido.");

    public static string MissingCodeParameter(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Error: missing or invalid 'code' parameter.",
            "Erro: parâmetro 'code' em falta ou inválido.");

    public static string MissingContainerImage(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Error: containerImage is not configured for custom runtime.",
            "Erro: containerImage não configurado para runtime custom.");

    public static string InvalidMcpToolName(string toolName, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Invalid MCP tool name: '{toolName}'.",
            $"Nome de tool MCP inválido: '{toolName}'.");

    public static string McpServerNotConfigured(string serverName, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"MCP server '{serverName}' is not configured for this tenant.",
            $"Servidor MCP '{serverName}' não configurado para este tenant.");

    public static string McpError(string serverName, string toolName, string message, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"MCP error ({serverName}/{toolName}): {message}",
            $"Erro MCP ({serverName}/{toolName}): {message}");

    public static string AcaContactError(string message, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Error contacting ACA Dynamic Sessions: {message}",
            $"Erro ao contactar ACA Dynamic Sessions: {message}");

    public static string SandboxContactError(string message, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"Error contacting Sandbox Executor: {message}",
            $"Erro ao contactar Sandbox Executor: {message}");

    public static string McpMockToolFailed(string server, string toolName) =>
        $"[mock:{server}] Tool '{toolName}' failed.";
}
