using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }

    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; init; }

    [JsonPropertyName("think")]
    public bool? Think { get; init; }

    [JsonPropertyName("mcp_servers")]
    public List<OllamaMcpServer>? McpServers { get; init; }
}

public record OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; init; }
}

public record OllamaOptions
{
    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    [JsonPropertyName("top_k")]
    public int? TopK { get; init; }

    [JsonPropertyName("num_ctx")]
    public int? NumCtx { get; init; }

    [JsonPropertyName("repeat_penalty")]
    public float? RepeatPenalty { get; init; }

    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; init; }

    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; init; }

    [JsonPropertyName("tfs_z")]
    public float? TfsZ { get; init; }

    [JsonPropertyName("mirostat")]
    public int? Mirostat { get; init; }
}

public record OllamaTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OllamaFunction Function);

public record OllamaFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parameters")] object? Parameters);

public record OllamaToolCall(
    [property: JsonPropertyName("function")] OllamaFunctionCall Function);

public record OllamaFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);

public record OllamaMcpServer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);
