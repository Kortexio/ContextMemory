using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }

    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; init; }

    /// <summary>
    /// When false, disables model thinking/reasoning (native Ollama <c>think</c>;
    /// OpenAI-compatible path maps to <c>reasoning_effort: none</c>).
    /// </summary>
    [JsonPropertyName("think")]
    public bool? Think { get; init; }
}
