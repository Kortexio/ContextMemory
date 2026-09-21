using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// Tool execution and agentic loop messages returned to the LLM (English only).
/// </summary>
public static class ToolExecutionMessages
{
    public static string ToolNotRegistered(string toolName, AppRuntimeConfig config) =>
        $"Tool '{toolName}' is not registered for this tenant.";

    public static string ToolNotRegisteredSelfHosted(string toolName, AppRuntimeConfig config) =>
        $"Tool '{toolName}' is not registered for this tenant (self-hosted-sandbox).";

    public static string AcaPoolNotConfigured(string runtime, AppRuntimeConfig config) =>
        $"Error: no ACA poolEndpoint configured for runtime '{runtime}'.";

    public static string SandboxEndpointNotConfigured(string runtime, AppRuntimeConfig config) =>
        $"Error: no sandboxEndpoint configured for runtime '{runtime}'.";

    public static string UnsupportedAcaRuntime(string runtime, AppRuntimeConfig config) =>
        $"ACA runtime '{runtime}' is not supported.";

    public static string UnsupportedSelfHostedRuntime(string runtime, AppRuntimeConfig config) =>
        $"Self-hosted runtime '{runtime}' is not supported.";

    public static string MissingCommandParameter(AppRuntimeConfig config) =>
        "Error: missing or invalid 'command' parameter.";

    public static string MissingCodeParameter(AppRuntimeConfig config) =>
        "Error: missing or invalid 'code' parameter.";

    public static string MissingContainerImage(AppRuntimeConfig config) =>
        "Error: containerImage is not configured for custom runtime.";

    public static string InvalidMcpToolName(string toolName, AppRuntimeConfig config) =>
        $"Invalid MCP tool name: '{toolName}'.";

    public static string McpServerNotConfigured(string serverName, AppRuntimeConfig config) =>
        $"MCP server '{serverName}' is not configured for this tenant.";

    public static string McpError(string serverName, string toolName, string message, AppRuntimeConfig config) =>
        $"MCP error ({serverName}/{toolName}): {message}";

    public static string AcaContactError(string message, AppRuntimeConfig config) =>
        $"Error contacting ACA Dynamic Sessions: {message}";

    public static string SandboxContactError(string message, AppRuntimeConfig config) =>
        $"Error contacting Sandbox Executor: {message}";

    public static string McpMockToolFailed(string server, string toolName) =>
        $"[mock:{server}] Tool '{toolName}' failed.";
}
