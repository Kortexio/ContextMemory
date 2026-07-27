using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record RegisterAppRequest
{
    [JsonPropertyName("appName")]
    public string AppName { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; init; } = "en-US";

    [JsonPropertyName("llmBackend")]
    public string LlmBackend { get; init; } = "ollama";

    [JsonPropertyName("llmModel")]
    public string LlmModel { get; init; } = "qwen3.5:9b";

    /// <summary>Optional per-app LLM base URL. Empty = host default for the chosen backend.</summary>
    [JsonPropertyName("llmEndpoint")]
    public string? LlmEndpoint { get; init; }

    /// <summary>Optional per-app LLM API key for OpenAI-compatible endpoints.</summary>
    [JsonPropertyName("llmApiKey")]
    public string? LlmApiKey { get; init; }

    [JsonPropertyName("promptPersona")]
    public string PromptPersona { get; init; } = string.Empty;
}

public record RegisterAppResponse
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "ready";
}

public record RegisteredAppRecord
{
    public required string AppId { get; init; }
    public required string ApiKey { get; init; }
    public required string AppName { get; init; }
    public required string Domain { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public bool IsActive { get; init; } = true;
}
