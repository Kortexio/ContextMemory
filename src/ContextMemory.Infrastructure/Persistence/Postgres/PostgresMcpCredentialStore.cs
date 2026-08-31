using System.Text.Json;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class PostgresMcpCredentialStore : IMcpCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresMcpCredentialStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<McpCredentialRecord?> GetAsync(
        string appId,
        string integrationName,
        string? credentialRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialRef))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.McpCredentials.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AppId == appId
                    && x.IntegrationName == integrationName
                    && x.CredentialRef == credentialRef,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return null;

        var payload = JsonSerializer.Deserialize<McpCredentialSecretPayload>(entity.SecretJson, JsonOptions)
                      ?? new McpCredentialSecretPayload();

        return ToRecord(entity, payload);
    }

    public async Task<IReadOnlyList<McpCredentialRecord>> ListAsync(
        string appId,
        string? integrationName = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.McpCredentials.AsNoTracking().Where(x => x.AppId == appId);
        if (!string.IsNullOrWhiteSpace(integrationName))
            query = query.Where(x => x.IntegrationName == integrationName);

        var rows = await query
            .OrderBy(x => x.IntegrationName)
            .ThenBy(x => x.CredentialRef)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(entity =>
            {
                var payload = JsonSerializer.Deserialize<McpCredentialSecretPayload>(entity.SecretJson, JsonOptions)
                              ?? new McpCredentialSecretPayload();
                return ToRecord(entity, payload);
            })
            .ToList();
    }

    private static McpCredentialRecord ToRecord(McpCredentialEntity entity, McpCredentialSecretPayload payload) =>
        new()
        {
            AppId = entity.AppId,
            IntegrationName = entity.IntegrationName,
            CredentialRef = entity.CredentialRef,
            AuthMode = entity.AuthMode,
            BearerToken = payload.BearerToken,
            ApiKey = payload.ApiKey,
            HeaderName = payload.HeaderName,
            OAuth = payload.OAuth,
            Env = payload.Env,
            UpdatedAt = entity.UpdatedAt
        };

    public async Task UpsertAsync(McpCredentialRecord record, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.McpCredentials
            .FirstOrDefaultAsync(
                x => x.AppId == record.AppId
                    && x.IntegrationName == record.IntegrationName
                    && x.CredentialRef == record.CredentialRef,
                cancellationToken)
            .ConfigureAwait(false);

        var payload = new McpCredentialSecretPayload
        {
            BearerToken = record.BearerToken,
            ApiKey = record.ApiKey,
            HeaderName = record.HeaderName,
            OAuth = record.OAuth,
            Env = record.Env
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        if (entity is null)
        {
            db.McpCredentials.Add(new McpCredentialEntity
            {
                AppId = record.AppId,
                IntegrationName = record.IntegrationName,
                CredentialRef = record.CredentialRef,
                AuthMode = record.AuthMode,
                SecretJson = json,
                UpdatedAt = record.UpdatedAt == default ? DateTimeOffset.UtcNow : record.UpdatedAt
            });
        }
        else
        {
            entity.AuthMode = record.AuthMode;
            entity.SecretJson = json;
            entity.UpdatedAt = record.UpdatedAt == default ? DateTimeOffset.UtcNow : record.UpdatedAt;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class McpCredentialSecretPayload
    {
        public string? BearerToken { get; init; }
        public string? ApiKey { get; init; }
        public string? HeaderName { get; init; }
        public McpOAuthCredential? OAuth { get; init; }
        public Dictionary<string, string>? Env { get; init; }
    }
}
