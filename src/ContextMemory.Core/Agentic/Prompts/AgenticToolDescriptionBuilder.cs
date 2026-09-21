using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

/// <summary>
/// Short tool schema blurbs for the function catalog.
/// Capability prose (sandbox facts, prefer-MCP, packages) lives in Admin skills — not here.
/// Admin may override via <see cref="ExecutionToolConfig"/> description fields when present.
/// </summary>
public static class AgenticToolDescriptionBuilder
{
    public static string BuildShellDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        _ = config;
        if (!string.IsNullOrWhiteSpace(execution?.Description))
            return execution.Description.Trim();

        return IsSelfHosted(execution)
            ? "Run a shell command in the self-hosted sandbox (ephemeral cwd; print to stdout)."
            : "Run a shell command in an isolated execution sandbox.";
    }

    public static string BuildPythonDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        _ = config;
        if (!string.IsNullOrWhiteSpace(execution?.Description))
            return execution.Description.Trim();

        return IsSelfHosted(execution)
            ? "Run Python in the self-hosted sandbox (ephemeral files; print to stdout)."
            : "Run Python in an isolated execution sandbox.";
    }

    public static string BuildNodeDescription(AppRuntimeConfig config, ExecutionToolConfig? execution = null)
    {
        _ = config;
        if (!string.IsNullOrWhiteSpace(execution?.Description))
            return execution.Description.Trim();

        return IsSelfHosted(execution)
            ? "Run Node.js in the self-hosted sandbox (ephemeral cwd)."
            : "Run Node.js in an isolated execution sandbox.";
    }

    public static string BuildContainerDescription(AppRuntimeConfig config, ExecutionToolConfig execution)
    {
        _ = config;
        if (!string.IsNullOrWhiteSpace(execution.Description))
            return execution.Description.Trim();

        var image = string.IsNullOrWhiteSpace(execution.ContainerImage)
            ? "custom container"
            : execution.ContainerImage;
        return $"Run a command in container '{image}'.";
    }

    public static string BuildMcpDescription(McpToolDefinition tool, AppRuntimeConfig config)
    {
        _ = config;
        var baseDesc = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description;
        return $"[MCP:{tool.ServerName}] {baseDesc}";
    }

    public static string BuildWikiSearchDescription(AppRuntimeConfig config)
    {
        _ = config;
        return "Search the app knowledge base (ingested docs). Optional asOf for point-in-time facts.";
    }

    private static bool IsSelfHosted(ExecutionToolConfig? execution) =>
        execution is not null
        && string.Equals(execution.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase);
}
