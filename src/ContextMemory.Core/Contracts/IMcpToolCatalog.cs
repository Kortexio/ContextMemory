using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Lists MCP tools available to a tenant integration.
/// </summary>
public interface IMcpToolCatalog
{
    Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);

    void Invalidate(string appId);
}
