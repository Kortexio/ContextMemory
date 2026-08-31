using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Profile;

public sealed class FileMcpCredentialStore : IMcpCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;

    public FileMcpCredentialStore(IOptions<ContextMemoryOptions> options)
    {
        var cfg = options.Value;
        _root = Path.Combine(Path.GetFullPath(cfg.DataPath, cfg.ContentRootPath), "mcp-credentials");
        Directory.CreateDirectory(_root);
    }

    public async Task<McpCredentialRecord?> GetAsync(
        string appId,
        string integrationName,
        string? credentialRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialRef))
            return null;

        var path = GetPath(appId, integrationName, credentialRef);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<McpCredentialRecord>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<McpCredentialRecord>> ListAsync(
        string appId,
        string? integrationName = null,
        CancellationToken cancellationToken = default)
    {
        var dir = GetAppDir(appId);
        if (!Directory.Exists(dir))
            return [];

        var records = new List<McpCredentialRecord>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<McpCredentialRecord>(json, JsonOptions);
            if (record is null)
                continue;
            if (!string.IsNullOrWhiteSpace(integrationName)
                && !string.Equals(record.IntegrationName, integrationName, StringComparison.OrdinalIgnoreCase))
                continue;
            records.Add(record);
        }

        return records
            .OrderBy(r => r.IntegrationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CredentialRef, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UpsertAsync(McpCredentialRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GetAppDir(record.AppId));
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(
                GetPath(record.AppId, record.IntegrationName, record.CredentialRef),
                json,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetAppDir(string appId) => Path.Combine(_root, appId);

    private string GetPath(string appId, string integrationName, string credentialRef) =>
        Path.Combine(GetAppDir(appId), $"{integrationName}--{credentialRef}.json");
}
