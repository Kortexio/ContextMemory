namespace ContextMemory.Core.Agentic.Mcp;

public sealed class McpCredentialRecord
{
    public required string AppId { get; init; }
    public required string IntegrationName { get; init; }
    public required string CredentialRef { get; init; }
    public required string AuthMode { get; init; }
    public string? BearerToken { get; init; }
    public string? ApiKey { get; init; }
    public string? HeaderName { get; init; }
    public McpOAuthCredential? OAuth { get; init; }
    public Dictionary<string, string>? Env { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class McpOAuthCredential
{
    public string TokenUrl { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string? Scope { get; init; }
    public string? Audience { get; init; }
}

public sealed class McpCatalogSyncResult
{
    public required string AppId { get; init; }
    public required string IntegrationName { get; init; }
    public int ToolCount { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
}

public sealed class McpNormalizedResult
{
    public string Summary { get; init; } = string.Empty;
    public Dictionary<string, string> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Raw { get; init; } = string.Empty;
    public bool Truncated { get; init; }
}
