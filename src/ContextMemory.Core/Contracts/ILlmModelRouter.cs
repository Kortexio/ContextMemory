using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>LLM workload kinds used by <see cref="ILlmModelRouter"/>.</summary>
public enum LlmTaskType
{
    Chat,
    Planning,
    ToolSelection,
    Synthesis,
    Vision,
    Compaction,
    Wiki
}

/// <summary>Input for task-aware model selection.</summary>
public sealed record ModelRoutingRequest(
    LlmTaskType Task,
    AppRuntimeConfig Config,
    bool RequiresVision = false,
    int EstimatedTokens = 0);

/// <summary>Resolved primary model plus optional backend / fallback.</summary>
public sealed record ResolvedLlmTarget(
    string Model,
    string? Backend = null,
    string? FallbackModel = null);

/// <summary>Selects an LLM target by task, tenant config, capabilities, and fallbacks (CM-7).</summary>
public interface ILlmModelRouter
{
    ResolvedLlmTarget Route(ModelRoutingRequest request);

    /// <summary>
    /// Ordered fallback chain: primary → WikiLlmModel → platform DefaultLlmModel (deduped).
    /// </summary>
    IReadOnlyList<string> FallbackChain(ModelRoutingRequest request);
}
