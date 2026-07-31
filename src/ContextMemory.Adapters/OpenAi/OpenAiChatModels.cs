using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMemory.Adapters.OpenAi;

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; init; }

    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    [JsonPropertyName("tools")]
    public List<OpenAiTool>? Tools { get; init; }
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunction Function { get; init; } = new();
}

internal sealed class OpenAiFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public object? Parameters { get; init; }
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunctionCall Function { get; init; } = new();
}

internal sealed class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>OpenAI expects a JSON string; some servers accept an object.</summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "{}";
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }
}

internal sealed class OpenAiChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; init; }

    [JsonPropertyName("delta")]
    public OpenAiChatMessage? Delta { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenAiStreamChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }
}

/// <summary>Public-facing OpenAI chat completion request (API layer can reuse the same shape).</summary>
public sealed class OpenAiCompatibleChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiCompatibleChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("stop")]
    public JsonElement? Stop { get; set; }

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenAiCompatibleTool>? Tools { get; set; }
}

public sealed class OpenAiCompatibleChatMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>String or multimodal array; stored as raw JSON for flexibility.</summary>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiCompatibleToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class OpenAiCompatibleTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiCompatibleFunction Function { get; set; } = new();
}

public sealed class OpenAiCompatibleFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }
}

public sealed class OpenAiCompatibleToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiCompatibleFunctionCall Function { get; set; } = new();
}

public sealed class OpenAiCompatibleFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

public sealed class OpenAiCompatibleChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAiCompatibleChoice> Choices { get; set; } = [];
}

public sealed class OpenAiCompatibleChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAiCompatibleChatMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiCompatibleChatMessage? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAiCompatibleModelsResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public List<OpenAiCompatibleModel> Data { get; set; } = [];
}

public sealed class OpenAiCompatibleModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "model";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; } = "contextmemory";
}
