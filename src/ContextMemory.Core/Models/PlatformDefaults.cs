using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record PlatformDefaults
{
    [JsonPropertyName("defaultWikiLlmModel")]
    public string DefaultWikiLlmModel { get; init; } = string.Empty;
}

public record PlatformDefaultsPatchRequest
{
    [JsonPropertyName("defaultWikiLlmModel")]
    public string? DefaultWikiLlmModel { get; init; }
}
