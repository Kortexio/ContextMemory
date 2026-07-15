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
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }
}

internal sealed class OpenAiChatChoice
{
    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; init; }

    [JsonPropertyName("delta")]
    public OpenAiChatMessage? Delta { get; init; }
}

internal sealed class OpenAiStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }
}
