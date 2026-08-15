using System.Collections.Concurrent;
using ContextMemory.Infrastructure.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

public sealed class McpToolCatalog : IMcpToolCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly McpJsonRpcClient _client;
    private readonly IMcpCatalogStore _store;
    private readonly IMcpToolSelector _selector;
    private readonly ILogger<McpToolCatalog> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public McpToolCatalog(
        McpJsonRpcClient client,
        IMcpCatalogStore store,
        IMcpToolSelector selector,
        ILogger<McpToolCatalog> logger)
    {
        _client = client;
        _store = store;
        _selector = selector;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var allTools = await GetAllToolsAsync(runtimeConfig, cancellationToken).ConfigureAwait(false);
        return _selector.SelectTools(runtimeConfig, allTools, userQuery, recentToolNames);
    }

    public async Task<IReadOnlyList<McpToolDefinition>> GetAllToolsAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        var integrations = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => i.Enabled)
            .Where(i => i.IsConfigured)
            .ToList();

        if (integrations.Count == 0)
            return [];

        IReadOnlyList<McpToolDefinition> raw;
        if (_cache.TryGetValue(runtimeConfig.AppId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            raw = cached.Tools;
        }
        else
        {
            var integrationNames = integrations.Select(i => i.Name).ToList();
            var storedTools = await _store.GetToolsAsync(runtimeConfig.AppId, integrationNames, cancellationToken).ConfigureAwait(false);
            if (storedTools.Count > 0)
            {
                _cache[runtimeConfig.AppId] = new CacheEntry(storedTools, DateTimeOffset.UtcNow.Add(CacheTtl));
                raw = storedTools;
            }
            else
            {
                var synced = await SyncAsync(runtimeConfig, cancellationToken).ConfigureAwait(false);
                var allTools = await _store.GetToolsAsync(runtimeConfig.AppId, integrationNames, cancellationToken).ConfigureAwait(false);
                _cache[runtimeConfig.AppId] = new CacheEntry(allTools, DateTimeOffset.UtcNow.Add(CacheTtl));
                _ = synced;
                raw = allTools;
            }
        }

        // Apply Admin allowlist/denylist on every read so the LLM never sees tools
        // that ExecuteAsync would reject (cache stays unfiltered for config changes).
        return McpToolAccess.FilterCatalog(runtimeConfig, raw);
    }

    public async Task<IReadOnlyList<McpCatalogSyncResult>> SyncAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        var integrations = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => i.Enabled)
            .Where(i => i.IsConfigured)
            .ToList();

        foreach (var server in integrations)
        {
            var syncedAt = DateTimeOffset.UtcNow;
            if (!AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(runtimeConfig, server))
            {
                _logger.LogWarning(
                    "MCP egress blocked for server {Server} ({Url}) app {AppId}",
                    server.Name,
                    server.Url,
                    runtimeConfig.AppId);
                await _store.ReplaceIntegrationToolsAsync(
                        runtimeConfig.AppId,
                        server.Name,
                        [],
                        "egress blocked",
                        syncedAt,
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            try
            {
                var tools = await _client.ListToolsAsync(runtimeConfig.AppId, server, cancellationToken).ConfigureAwait(false);
                await _store.ReplaceIntegrationToolsAsync(
                        runtimeConfig.AppId,
                        server.Name,
                        tools,
                        error: null,
                        syncedAt,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list MCP tools from server {Server} for app {AppId}", server.Name, runtimeConfig.AppId);
                await _store.ReplaceIntegrationToolsAsync(
                        runtimeConfig.AppId,
                        server.Name,
                        [],
                        ex.Message,
                        syncedAt,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await _store
            .PruneIntegrationsAsync(runtimeConfig.AppId, integrations.Select(i => i.Name), cancellationToken)
            .ConfigureAwait(false);

        Invalidate(runtimeConfig.AppId);
        return await _store.GetSyncStatusAsync(runtimeConfig.AppId, cancellationToken).ConfigureAwait(false);
    }

    public void Invalidate(string appId) => _cache.TryRemove(appId, out _);

    private sealed record CacheEntry(IReadOnlyList<McpToolDefinition> Tools, DateTimeOffset ExpiresAt);
}
