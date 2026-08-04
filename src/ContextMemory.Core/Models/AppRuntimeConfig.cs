using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Models;

public record AppRuntimeConfig
{
    public required string AppId { get; init; }
    public string BasePersona { get; init; } = string.Empty;
    public string BusinessRules { get; init; } = string.Empty;
    public string FormatRules { get; init; } = string.Empty;
    public string WikiSchema { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = "en-US";
    public string LlmModel { get; init; } = "qwen3.5:9b";
    public string LlmBackend { get; init; } = "ollama";

    /// <summary>
    /// Optional per-app LLM base URL. Empty = use the host default for <see cref="LlmBackend"/>
    /// (<c>OllamaEndpoint</c> / <c>LmStudioEndpoint</c> / <c>OpenAiEndpoint</c>).
    /// </summary>
    public string LlmEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// Optional per-app API key for OpenAI-compatible backends. Empty = use host <c>OpenAiApiKey</c>.
    /// </summary>
    public string LlmApiKey { get; init; } = string.Empty;

    public int MaxHistoryMessages { get; init; } = 20;
    public int MaxWikiContextChars { get; init; }
    public long WikiCompactionThresholdBytes { get; init; }
    public int WikiCompactionMinPages { get; init; }
    public bool StreamingEnabled { get; init; } = true;

    /// <summary>
    /// When true, allow model "thinking" / reasoning tokens (Qwen3, etc.).
    /// Default false — Ollama otherwise auto-enables thinking on capable models via /v1.
    /// </summary>
    public bool LlmThinkEnabled { get; init; }

    public RateLimitConfig RateLimits { get; init; } = new();
    public WebSearchConfig WebSearch { get; init; } = WebSearchConfig.Disabled;
    public AgenticConfig Agentic { get; init; } = AgenticConfig.Disabled;

    /// <summary>
    /// Runtime-only resolved skills/guardrails (not persisted in ConfigJson).
    /// Populated by <c>IAgenticPolicyPackResolver</c> at chat start.
    /// </summary>
    public ResolvedAgenticPolicy ResolvedPolicy { get; init; } = new();

    /// <summary>When true, exposes the built-in <c>wiki_search</c> tool for app-scoped global docs.</summary>
    public bool GlobalWikiEnabled { get; init; } = true;

    /// <summary>Max chars returned by <c>wiki_search</c> (0 = service default).</summary>
    public int MaxGlobalWikiToolChars { get; init; }

    public bool AgenticEnabled =>
        (Agentic.Enabled && Agentic.HasAnyTools) || GlobalWikiEnabled;
}
