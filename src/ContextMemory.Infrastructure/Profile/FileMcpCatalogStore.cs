using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Profile;

public sealed class FileMcpCatalogStore : IMcpCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;

    public FileMcpCatalogStore(IOptions<ContextMemoryOptions> options)
    {
        var cfg = options.Value;
        _root = Path.Combine(Path.GetFullPath(cfg.DataPath, cfg.ContentRootPath), "mcp-catalog");
        Directory.CreateDirectory(_root);
    }

    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        string appId,
        IEnumerable<string>? integrationNames = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<McpToolDefinition>();
        var names = integrationNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appDir = GetAppDir(appId);
        if (!Directory.Exists(appDir))
            return list;

        foreach (var file in Directory.EnumerateFiles(appDir, "*.json"))
        {
            var integrationName = Path.GetFileNameWithoutExtension(file);
            if (names is not null && names.Count > 0 && !names.Contains(integrationName))
                continue;

            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<IntegrationCatalogFile>(json, JsonOptions);
            if (payload?.Tools is { Count: > 0 })
                list.AddRange(payload.Tools);
        }

        return list;
    }

    public async Task<IReadOnlyList<McpCatalogSyncResult>> GetSyncStatusAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<McpCatalogSyncResult>();
        var appDir = GetAppDir(appId);
        if (!Directory.Exists(appDir))
            return list;

        foreach (var file in Directory.EnumerateFiles(appDir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<IntegrationCatalogFile>(json, JsonOptions);
            if (payload?.Status is not null)
                list.Add(payload.Status);
        }

        return list;
    }

    public async Task ReplaceIntegrationToolsAsync(
        string appId,
        string integrationName,
        IReadOnlyList<McpToolDefinition> tools,
        string? error,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GetAppDir(appId));
        var payload = new IntegrationCatalogFile
        {
            Status = new McpCatalogSyncResult
            {
                AppId = appId,
                IntegrationName = integrationName,
                ToolCount = tools.Count,
                Success = string.IsNullOrWhiteSpace(error),
                Error = error,
                SyncedAt = syncedAt
            },
            Tools = tools.ToList()
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(GetPath(appId, integrationName), json, cancellationToken).ConfigureAwait(false);
    }

    public Task PruneIntegrationsAsync(
        string appId,
        IEnumerable<string> keepIntegrationNames,
        CancellationToken cancellationToken = default)
    {
        var keep = keepIntegrationNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appDir = GetAppDir(appId);
        if (!Directory.Exists(appDir))
            return Task.CompletedTask;

        foreach (var file in Directory.EnumerateFiles(appDir, "*.json"))
        {
            var integrationName = Path.GetFileNameWithoutExtension(file);
            if (!keep.Contains(integrationName))
                File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private string GetAppDir(string appId) => Path.Combine(_root, appId);

    private string GetPath(string appId, string integrationName) => Path.Combine(GetAppDir(appId), integrationName + ".json");

    private sealed class IntegrationCatalogFile
    {
        public McpCatalogSyncResult? Status { get; init; }
        public List<McpToolDefinition> Tools { get; init; } = [];
    }
}
