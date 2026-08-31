using System.Text.Json.Serialization;
using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Models;

public sealed class McpCredentialUpsertRequest
{
    [JsonPropertyName("integrationName")]
    public required string IntegrationName { get; init; }

    [JsonPropertyName("credentialRef")]
    public required string CredentialRef { get; init; }

    [JsonPropertyName("authMode")]
    public required string AuthMode { get; init; }

    [JsonPropertyName("bearerToken")]
    public string? BearerToken { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    [JsonPropertyName("headerName")]
    public string? HeaderName { get; init; }

    [JsonPropertyName("oauth")]
    public McpOAuthConfig? OAuth { get; init; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public sealed class McpCredentialAdminDto
{
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    [JsonPropertyName("integrationName")]
    public string IntegrationName { get; init; } = string.Empty;

    [JsonPropertyName("credentialRef")]
    public string CredentialRef { get; init; } = string.Empty;

    [JsonPropertyName("authMode")]
    public string AuthMode { get; init; } = string.Empty;

    [JsonPropertyName("bearerToken")]
    public string? BearerToken { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    [JsonPropertyName("headerName")]
    public string? HeaderName { get; init; }

    [JsonPropertyName("oauth")]
    public McpOAuthConfig? OAuth { get; init; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class McpCatalogSyncRequest
{
    [JsonPropertyName("integrationName")]
    public string? IntegrationName { get; init; }
}

public sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("transport")]
    public string Transport { get; init; } = "http";

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public List<string> Args { get; init; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("credentialRef")]
    public string? CredentialRef { get; init; }

    [JsonPropertyName("toolAllowlist")]
    public List<string> ToolAllowlist { get; init; } = [];

    [JsonPropertyName("toolDenylist")]
    public List<string> ToolDenylist { get; init; } = [];

    [JsonPropertyName("requiresConfirmation")]
    public List<string> RequiresConfirmation { get; init; } = [];
}
