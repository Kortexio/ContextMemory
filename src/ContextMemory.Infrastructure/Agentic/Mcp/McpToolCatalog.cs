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
    private readonly ILogger<McpToolCatalog> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public McpToolCatalog(McpJsonRpcClient client, ILogger<McpToolCatalog> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        var integrations = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Url))
            .ToList();

        if (integrations.Count == 0)
            return [];

        if (_cache.TryGetValue(runtimeConfig.AppId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Tools;

        var allTools = new List<McpToolDefinition>();

        foreach (var server in integrations)
        {
            if (!AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(runtimeConfig, server))
            {
                _logger.LogWarning(
                    "MCP egress blocked for server {Server} ({Url}) app {AppId}",
                    server.Name,
                    server.Url,
                    runtimeConfig.AppId);
                continue;
            }

            try
            {
                var tools = await _client.ListToolsAsync(server, cancellationToken).ConfigureAwait(false);
                allTools.AddRange(tools);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list MCP tools from server {Server} for app {AppId}", server.Name, runtimeConfig.AppId);
            }
        }

        _cache[runtimeConfig.AppId] = new CacheEntry(allTools, DateTimeOffset.UtcNow.Add(CacheTtl));
        return allTools;
    }

    public void Invalidate(string appId) => _cache.TryRemove(appId, out _);

    private sealed record CacheEntry(IReadOnlyList<McpToolDefinition> Tools, DateTimeOffset ExpiresAt);
}
