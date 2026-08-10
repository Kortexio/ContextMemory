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
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full MCP catalog for the app (no top-K selector). Used by <c>tool_describe</c> exact lookups.
    /// </summary>
    Task<IReadOnlyList<McpToolDefinition>> GetAllToolsAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);

    void Invalidate(string appId);

    Task<IReadOnlyList<McpCatalogSyncResult>> SyncAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);
}
