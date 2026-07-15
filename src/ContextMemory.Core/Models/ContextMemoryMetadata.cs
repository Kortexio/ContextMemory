using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record ContextMemoryMetadata
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    [JsonPropertyName("agentic")]
    public AgenticStreamMetadata? Agentic { get; init; }
}
