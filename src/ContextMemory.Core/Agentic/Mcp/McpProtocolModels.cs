using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMemory.Core.Agentic.Mcp;

internal sealed class McpJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public object? Params { get; init; }
}

internal sealed class McpJsonRpcNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("method")]
    public required string Method { get; init; }
}

internal sealed class McpJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; init; }

    [JsonPropertyName("id")]
    public JsonElement Id { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public McpJsonRpcError? Error { get; init; }
}

internal sealed class McpJsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class McpToolDefinition
{
    public required string ServerName { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public object? InputSchema { get; init; }

    public string QualifiedName => McpToolNaming.ToQualifiedName(ServerName, Name);
}
