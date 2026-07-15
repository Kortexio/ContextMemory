using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record OllamaResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; init; }

    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDuration { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }

    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; init; }

    [JsonPropertyName("context")]
    public List<int>? Context { get; init; }

    [JsonPropertyName("context_memory")]
    public ContextMemoryMetadata? ContextMemory { get; init; }
}
