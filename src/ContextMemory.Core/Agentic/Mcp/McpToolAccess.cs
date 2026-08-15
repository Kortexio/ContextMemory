using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// Allowlist / denylist checks shared by catalog exposure and MCP execution.
/// Tools must not be offered to the LLM if they would be rejected at execute time.
/// </summary>
public static class McpToolAccess
{
    public static bool IsPermitted(IntegrationToolConfig? server, string mcpToolName)
    {
        if (server is null || string.IsNullOrWhiteSpace(mcpToolName))
            return false;

        if (server.ToolDenylist.Any(t =>
                string.Equals(t, mcpToolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (server.ToolAllowlist.Count > 0
            && !server.ToolAllowlist.Any(t =>
                string.Equals(t, mcpToolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    public static IReadOnlyList<McpToolDefinition> FilterCatalog(
        AppRuntimeConfig runtimeConfig,
        IReadOnlyList<McpToolDefinition> tools)
    {
        if (tools.Count == 0)
            return tools;

        var servers = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => i.Enabled)
            .ToList();

        if (servers.Count == 0)
            return tools;

        var filtered = new List<McpToolDefinition>(tools.Count);
        foreach (var tool in tools)
        {
            var server = servers.FirstOrDefault(s =>
                McpToolNaming.ServerNamesMatch(s.Name, tool.ServerName));
            if (server is null)
                continue;

            if (!IsPermitted(server, tool.Name))
                continue;

            filtered.Add(tool);
        }

        return filtered;
    }
}
