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

    /// <summary>
    /// Summary / digest model (dynamic context discovery): session wiki maintainer/compactor and Global Wiki digests.
    /// Empty = platform default, then <see cref="LlmModel"/>. Prefer a smaller/cheaper model than chat.
    /// </summary>
    public string WikiLlmModel { get; init; } = string.Empty;

    /// <summary>
    /// Run wiki maintainer LLM every N assistant turns (default 3). Values ≤0 are treated as 1 at resolve time.
    /// </summary>
    public int WikiUpdateEveryNTurns { get; init; } = 3;

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

    public int MaxHistoryMessages { get; init; } = 6;
    public int MaxWikiContextChars { get; init; }

    /// <summary>Max chars of Global Wiki digests injected into the system prompt (0 = host default).</summary>
    public int MaxDigestContextChars { get; init; }

    /// <summary>Top-K digests for pre-LLM discovery inject (0 = host default).</summary>
    public int DigestTopK { get; init; }

    /// <summary>Max chars of a tool observation in the agent loop (0 = host default). Longer output is truncated with a pointer.</summary>
    public int MaxToolObservationChars { get; init; }

    /// <summary>Estimated token budget for mid-turn agent context compaction (0 = host default).</summary>
    public int MaxContextTokens { get; init; }

    public long WikiCompactionThresholdBytes { get; init; }
    public int WikiCompactionMinPages { get; init; }
    public bool StreamingEnabled { get; init; } = true;

    /// <summary>
    /// When true, allow model "thinking" / reasoning tokens (Qwen3, etc.).
    /// Default false — Ollama otherwise auto-enables thinking on capable models via /v1.
    /// </summary>
    public bool LlmThinkEnabled { get; init; }

    /// <summary>Tenant defaults for generation payload (temperature, num_ctx, num_predict, …).</summary>
    public LlmGenerationConfig LlmOptions { get; init; } = new();

    public RateLimitConfig RateLimits { get; init; } = new();
    public WebSearchConfig WebSearch { get; init; } = WebSearchConfig.Disabled;
    public AgenticConfig Agentic { get; init; } = AgenticConfig.Disabled;

    /// <summary>
    /// Runtime-only resolved skills/guardrails (not persisted in ConfigJson).
    /// Populated by <c>IAgenticPolicyPackResolver</c> at chat start.
    /// </summary>
    public ResolvedAgenticPolicy ResolvedPolicy { get; init; } = new();

    /// <summary>
    /// When true, injects top-K Global Wiki digests into the system prompt (DB-first discovery)
    /// and exposes <c>wiki_search</c> when agentic tools are otherwise enabled.
    /// Does not force the agentic loop by itself.
    /// </summary>
    public bool GlobalWikiEnabled { get; init; } = true;

    /// <summary>Max chars returned by <c>wiki_search</c> full-body hydrate (0 = service default).</summary>
    public int MaxGlobalWikiToolChars { get; init; }

    /// <summary>Agentic loop only when sandbox/MCP/tools are configured — not merely because Global Wiki is on.</summary>
    public bool AgenticEnabled =>
        Agentic.Enabled && Agentic.HasAnyTools;
}
