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

    [JsonPropertyName("promoted_prose_tool_calls")]
    public int PromotedProseToolCalls { get; init; }

    [JsonPropertyName("resolved_prompt_profile")]
    public string? ResolvedPromptProfile { get; init; }

    [JsonPropertyName("harness_mode")]
    public string? HarnessMode { get; init; }

    [JsonPropertyName("schema_repair_level")]
    public string? SchemaRepairLevel { get; init; }

    [JsonPropertyName("family_lifts")]
    public int FamilyLifts { get; init; }

    [JsonPropertyName("arg_repairs")]
    public int ArgRepairs { get; init; }

    [JsonPropertyName("finish_gate_rejects")]
    public int FinishGateRejects { get; init; }

    [JsonPropertyName("refunds_used")]
    public int RefundsUsed { get; init; }

    [JsonPropertyName("continue_extensions")]
    public int ContinueExtensions { get; init; }

    public static DiscoveryTelemetry FromCounts(
        int staticPromptChars,
        int discoveryFetchedChars,
        int toolObservationChars,
        int compactionCount,
        int llmCalls,
        int promotedProseToolCalls = 0,
        string? resolvedPromptProfile = null,
        string? harnessMode = null,
        string? schemaRepairLevel = null,
        int familyLifts = 0,
        int argRepairs = 0,
        int finishGateRejects = 0,
        int refundsUsed = 0,
        int continueExtensions = 0)
    {
        var denom = staticPromptChars + discoveryFetchedChars;
        return new DiscoveryTelemetry
        {
            StaticPromptChars = staticPromptChars,
            DiscoveryFetchedChars = discoveryFetchedChars,
            ToolObservationChars = toolObservationChars,
            CompactionCount = compactionCount,
            LlmCalls = llmCalls,
            DiscoveryRatio = denom > 0 ? (double)discoveryFetchedChars / denom : null,
            PromotedProseToolCalls = promotedProseToolCalls,
            ResolvedPromptProfile = resolvedPromptProfile,
            HarnessMode = harnessMode,
            SchemaRepairLevel = schemaRepairLevel,
            FamilyLifts = familyLifts,
            ArgRepairs = argRepairs,
            FinishGateRejects = finishGateRejects,
            RefundsUsed = refundsUsed,
            ContinueExtensions = continueExtensions
        };
    }
}
