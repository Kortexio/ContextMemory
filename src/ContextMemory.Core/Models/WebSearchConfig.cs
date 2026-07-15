using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record WebSearchConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "heuristic";

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "tavily";

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; init; } = 5;

    [JsonPropertyName("maxContextChars")]
    public int MaxContextChars { get; init; } = 3000;

    [JsonPropertyName("persistToWiki")]
    public bool PersistToWiki { get; init; }

    [JsonPropertyName("logWebSearch")]
    public bool LogWebSearch { get; init; } = true;

    public bool IsActive =>
        Enabled && !string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase);

    public static WebSearchConfig Disabled { get; } = new();
}

public record WebSearchConfigPatch
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("maxResults")]
    public int? MaxResults { get; init; }

    [JsonPropertyName("maxContextChars")]
    public int? MaxContextChars { get; init; }

    [JsonPropertyName("persistToWiki")]
    public bool? PersistToWiki { get; init; }

    [JsonPropertyName("logWebSearch")]
    public bool? LogWebSearch { get; init; }
}
