using ContextMemory.Core.Agentic.Mcp;

namespace ContextMemory.Core.Contracts;

public interface IMcpCatalogStore
{
    Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        string appId,
        IEnumerable<string>? integrationNames = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpCatalogSyncResult>> GetSyncStatusAsync(
        string appId,
        CancellationToken cancellationToken = default);

    Task ReplaceIntegrationToolsAsync(
        string appId,
        string integrationName,
        IReadOnlyList<McpToolDefinition> tools,
        string? error,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default);
}
