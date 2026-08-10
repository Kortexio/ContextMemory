using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

/// <summary>Per-turn token/char accounting for static vs discovery context (Cursor-style harness proof).</summary>
public sealed record DiscoveryTelemetry
{
    [JsonPropertyName("static_prompt_chars")]
    public int StaticPromptChars { get; init; }

    [JsonPropertyName("discovery_fetched_chars")]
    public int DiscoveryFetchedChars { get; init; }

    [JsonPropertyName("tool_observation_chars")]
    public int ToolObservationChars { get; init; }

    [JsonPropertyName("compaction_count")]
    public int CompactionCount { get; init; }

    [JsonPropertyName("llm_calls")]
    public int LlmCalls { get; init; }

    [JsonPropertyName("discovery_ratio")]
    public double? DiscoveryRatio { get; init; }

    public static DiscoveryTelemetry FromCounts(
        int staticPromptChars,
        int discoveryFetchedChars,
        int toolObservationChars,
        int compactionCount,
        int llmCalls)
    {
        var denom = staticPromptChars + discoveryFetchedChars;
        return new DiscoveryTelemetry
        {
            StaticPromptChars = staticPromptChars,
            DiscoveryFetchedChars = discoveryFetchedChars,
            ToolObservationChars = toolObservationChars,
            CompactionCount = compactionCount,
            LlmCalls = llmCalls,
            DiscoveryRatio = denom > 0 ? (double)discoveryFetchedChars / denom : null
        };
    }
}
