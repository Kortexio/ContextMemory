using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresMcpCatalogStore : IMcpCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresMcpCatalogStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        string appId,
        IEnumerable<string>? integrationNames = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.McpCatalogTools.AsNoTracking().Where(x => x.AppId == appId);
        if (integrationNames is not null)
        {
            var names = integrationNames.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Count > 0)
                query = query.Where(x => names.Contains(x.IntegrationName));
        }

        var rows = await query.OrderBy(x => x.IntegrationName).ThenBy(x => x.ToolName).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => new McpToolDefinition
        {
            ServerName = x.IntegrationName,
            Name = x.ToolName,
            Description = x.Description,
            InputSchema = string.IsNullOrWhiteSpace(x.InputSchemaJson)
                ? null
                : JsonSerializer.Deserialize<object>(x.InputSchemaJson, JsonOptions)
        }).ToList();
    }

    public async Task<IReadOnlyList<McpCatalogSyncResult>> GetSyncStatusAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.McpCatalogSync.AsNoTracking()
            .Where(x => x.AppId == appId)
            .OrderBy(x => x.IntegrationName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => new McpCatalogSyncResult
        {
            AppId = x.AppId,
            IntegrationName = x.IntegrationName,
            ToolCount = x.ToolCount,
            Success = string.Equals(x.SyncStatus, "ok", StringComparison.OrdinalIgnoreCase),
            Error = x.LastError,
            SyncedAt = x.LastSyncedAt
        }).ToList();
    }

    public async Task ReplaceIntegrationToolsAsync(
        string appId,
        string integrationName,
        IReadOnlyList<McpToolDefinition> tools,
        string? error,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.McpCatalogTools
            .Where(x => x.AppId == appId && x.IntegrationName == integrationName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
            db.McpCatalogTools.RemoveRange(existing);

        foreach (var tool in tools)
        {
            db.McpCatalogTools.Add(new McpCatalogToolEntity
            {
                AppId = appId,
                IntegrationName = integrationName,
                QualifiedName = tool.QualifiedName,
                ToolName = tool.Name,
                Description = tool.Description ?? string.Empty,
                InputSchemaJson = JsonSerializer.Serialize(tool.InputSchema ?? new { }, JsonOptions),
                CapabilitiesJson = "[]",
                LastSyncedAt = syncedAt
            });
        }

        var sync = await db.McpCatalogSync
            .FirstOrDefaultAsync(x => x.AppId == appId && x.IntegrationName == integrationName, cancellationToken)
            .ConfigureAwait(false);
        if (sync is null)
        {
            db.McpCatalogSync.Add(new McpCatalogSyncEntity
            {
                AppId = appId,
                IntegrationName = integrationName,
                ToolCount = tools.Count,
                SyncStatus = string.IsNullOrWhiteSpace(error) ? "ok" : "error",
                LastError = error,
                LastSyncedAt = syncedAt
            });
        }
        else
        {
            sync.ToolCount = tools.Count;
            sync.SyncStatus = string.IsNullOrWhiteSpace(error) ? "ok" : "error";
            sync.LastError = error;
            sync.LastSyncedAt = syncedAt;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PruneIntegrationsAsync(
        string appId,
        IEnumerable<string> keepIntegrationNames,
        CancellationToken cancellationToken = default)
    {
        var keep = keepIntegrationNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var tools = await db.McpCatalogTools.Where(x => x.AppId == appId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sync = await db.McpCatalogSync.Where(x => x.AppId == appId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orphanTools = tools.Where(x => !keep.Contains(x.IntegrationName)).ToList();
        var orphanSync = sync.Where(x => !keep.Contains(x.IntegrationName)).ToList();
        if (orphanTools.Count == 0 && orphanSync.Count == 0)
            return;

        if (orphanTools.Count > 0)
            db.McpCatalogTools.RemoveRange(orphanTools);
        if (orphanSync.Count > 0)
            db.McpCatalogSync.RemoveRange(orphanSync);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
