using System.Text.Json.Serialization;
using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Models;

public record AppConfigFile
{
    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; init; } = "en-US";

    [JsonPropertyName("llmModel")]
    public string LlmModel { get; init; } = "qwen3.5:9b";

    [JsonPropertyName("llmBackend")]
    public string LlmBackend { get; init; } = "ollama";

    [JsonPropertyName("maxHistoryMessages")]
    public int MaxHistoryMessages { get; init; } = 20;

    [JsonPropertyName("maxWikiContextChars")]
    public int MaxWikiContextChars { get; init; }

    [JsonPropertyName("wikiCompactionThresholdBytes")]
    public long WikiCompactionThresholdBytes { get; init; }

    [JsonPropertyName("wikiCompactionMinPages")]
    public int WikiCompactionMinPages { get; init; }

    [JsonPropertyName("streamingEnabled")]
    public bool StreamingEnabled { get; init; } = true;

    [JsonPropertyName("rateLimits")]
    public RateLimitConfig? RateLimits { get; init; }

    [JsonPropertyName("webSearch")]
    public WebSearchConfig? WebSearch { get; init; }

    [JsonPropertyName("agentic")]
    public AgenticConfig? Agentic { get; init; }
}

public record AppConfigPatchRequest
{
    [JsonPropertyName("defaultLanguage")]
    public string? DefaultLanguage { get; init; }

    [JsonPropertyName("llmModel")]
    public string? LlmModel { get; init; }

    [JsonPropertyName("llmBackend")]
    public string? LlmBackend { get; init; }

    [JsonPropertyName("maxHistoryMessages")]
    public int? MaxHistoryMessages { get; init; }

    [JsonPropertyName("maxWikiContextChars")]
    public int? MaxWikiContextChars { get; init; }

    [JsonPropertyName("wikiCompactionThresholdBytes")]
    public long? WikiCompactionThresholdBytes { get; init; }

    [JsonPropertyName("wikiCompactionMinPages")]
    public int? WikiCompactionMinPages { get; init; }

    [JsonPropertyName("streamingEnabled")]
    public bool? StreamingEnabled { get; init; }

    [JsonPropertyName("basePersona")]
    public string? BasePersona { get; init; }

    [JsonPropertyName("businessRules")]
    public string? BusinessRules { get; init; }

    [JsonPropertyName("formatRules")]
    public string? FormatRules { get; init; }

    [JsonPropertyName("wikiSchema")]
    public string? WikiSchema { get; init; }

    [JsonPropertyName("webSearch")]
    public WebSearchConfigPatch? WebSearch { get; init; }

    [JsonPropertyName("rateLimits")]
    public RateLimitConfig? RateLimits { get; init; }

    [JsonPropertyName("agentic")]
    public AgenticConfig? Agentic { get; init; }
}
