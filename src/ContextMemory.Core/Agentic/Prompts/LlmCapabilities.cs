using System.Text.RegularExpressions;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

/// <summary>Harness intensity for multi-model agentic loops.</summary>
public enum ModelHarnessMode
{
    Weak,
    Strong
}

/// <summary>
/// Protocol / harness knobs derived from the resolved prompt profile, model size, and LLM backend.
/// Distinct from guardrails (policy): these adapt wire format and repair behaviour to the model family.
/// </summary>
public sealed record LlmCapabilities(
    ModelHarnessMode HarnessMode,
    bool PreferNativeToolCalls,
    bool PreferClientSideToolParsing,
    bool EnableProseToolCallPromotion,
    bool SanitizeSchemasAggressively,
    bool SupportsOpenAiJsonFormat,
    bool InlineEvidenceRules,
    bool PreferSkillDiscovery,
    bool SupportsVision,
    int MaxMcpToolsHint,
    string? DefaultToolChoice);

public static partial class LlmCapabilitiesResolver
{
    public static LlmCapabilities From(AppRuntimeConfig config)
    {
        var profile = AgenticPromptProfileResolver.Resolve(config);
        var backend = (config.LlmBackend ?? "ollama").Trim().ToLowerInvariant();
        var openAiCompat = IsOpenAiCompatibleBackend(backend);
        var mode = ResolveHarnessMode(config, profile);

        var weak = mode == ModelHarnessMode.Weak;
        // Harness-based (not server/app specific): Weak models in the Qwen-family profile
        // often emit malformed native tool_calls across backends. Prefer client-side JSON
        // catalog for that profile on any llmBackend. Other Weak profiles keep native tools[].
        var clientSideTools = weak && profile == AgenticPromptProfile.Qwen;
        var supportsVision = ResolveSupportsVision(config);

        return new LlmCapabilities(
            HarnessMode: mode,
            PreferNativeToolCalls: !weak && profile is AgenticPromptProfile.OpenAi
                or AgenticPromptProfile.Claude
                or AgenticPromptProfile.ComposerLike,
            PreferClientSideToolParsing: clientSideTools,
            EnableProseToolCallPromotion: true,
            SanitizeSchemasAggressively: weak,
            SupportsOpenAiJsonFormat: openAiCompat
                || profile is AgenticPromptProfile.OpenAi or AgenticPromptProfile.ComposerLike,
            InlineEvidenceRules: weak,
            PreferSkillDiscovery: !weak,
            SupportsVision: supportsVision,
            // Weak harness burns context the same whether tools are inlined or native tools[].
            // Strong / frontier paths keep full tenant max (up to AbsoluteMax).
            MaxMcpToolsHint: weak ? WeakPromptMaxMcpTools : int.MaxValue,
            DefaultToolChoice: "auto");
        }

    public static bool ResolveSupportsVision(AppRuntimeConfig config)
    {
        if (config.Agentic.Tools.Vision.ForceEnable)
            return true;

        var model = config.LlmModel ?? string.Empty;
        return LooksLikeVisionModel(model);
    }

    public static bool LooksLikeVisionModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        return ContainsIgnore(
            model,
            "llava",
            "vision",
            "gpt-4o",
            "gpt-4.1",
            "gpt-4-turbo",
            "gpt-5",
            "claude-3",
            "claude-sonnet-4",
            "claude-opus-4",
            "gemini",
            "qwen2-vl",
            "qwen2.5-vl",
            "qwen3-vl",
            "qwen-vl",
            "minicpm-v",
            "moondream",
            "bakllava");
    }

    /// <summary>
    /// Absolute ceiling for MCP tools offered to the model per turn (Qwen dumps the selection into the system prompt).
    /// Tenant config above this is clamped at resolve time so oversized catalogs cannot blow the context window.
    /// </summary>
    public const int AbsoluteMaxMcpToolsPerTurn = 12;

    /// <summary>
    /// Soft cap for Weak harness models (local Qwen/Bonsai on Ollama, llama.cpp, LM Studio, …).
    /// Applies whether tools are inlined in the system prompt or sent as native <c>tools[]</c>.
    /// Still selects via <c>McpToolSelector</c> — does not omit MCP or reintroduce lazy tool_search.
    /// </summary>
    public const int WeakPromptMaxMcpTools = 6;

    /// <summary>Alias kept for older call sites / tests.</summary>
    public const int ClientSidePromptMaxMcpTools = WeakPromptMaxMcpTools;

    /// <summary>
    /// Effective max MCP tools from tenant config (<c>maxMcpToolsPerTurn</c>). Default 12 when unset; hard-capped at
    /// <see cref="AbsoluteMaxMcpToolsPerTurn"/>. Weak harness also clamps to <see cref="WeakPromptMaxMcpTools"/>.
    /// </summary>
    public static int ResolveMaxMcpTools(AppRuntimeConfig config)
    {
        var configured = config.Agentic.Tools.MaxMcpToolsPerTurn > 0
            ? config.Agentic.Tools.MaxMcpToolsPerTurn
            : AbsoluteMaxMcpToolsPerTurn;
        var caps = From(config);
        return Math.Clamp(Math.Min(configured, caps.MaxMcpToolsHint), 1, AbsoluteMaxMcpToolsPerTurn);
    }

    /// <summary>
    /// Effective max iterations: explicit guardrail value, else profile default.
    /// </summary>
    public static int ResolveMaxIterations(AppRuntimeConfig config)
    {
        var configured = config.Agentic.Guardrails.MaxIterations;
        if (configured > 0)
            return configured;

        var profile = AgenticPromptProfileResolver.Resolve(config);
        return AgenticPromptProfileResolver.DefaultMaxIterations(profile);
    }

    public static ModelHarnessMode ResolveHarnessMode(AppRuntimeConfig config, AgenticPromptProfile? profile = null)
    {
        var explicitMode = config.Agentic.HarnessMode?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitMode)
            && !explicitMode.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return ParseHarnessMode(explicitMode);
        }

        profile ??= AgenticPromptProfileResolver.Resolve(config);
        var model = config.LlmModel ?? string.Empty;

        var mode = profile switch
        {
            AgenticPromptProfile.OpenAi or AgenticPromptProfile.Claude or AgenticPromptProfile.ComposerLike
                => ModelHarnessMode.Strong,
            AgenticPromptProfile.Qwen or AgenticPromptProfile.Ollama
                => ModelHarnessMode.Weak,
            _ => ModelHarnessMode.Weak
        };

        // Size / name hints (secondary). Do not promote Qwen/Bonsai to Strong solely for 27B.
        if (LooksLikeFrontierName(model))
            return ModelHarnessMode.Strong;

        var billions = TryParseBillions(model);
        if (billions is null)
            return mode;

        if (billions <= 14)
            return ModelHarnessMode.Weak;

        if (billions >= 32
            && profile is not AgenticPromptProfile.Qwen
            && !ContainsIgnore(model, "bonsai", "qwen", "granite", "gemma"))
        {
            return ModelHarnessMode.Strong;
        }

        // 15B–31B: keep profile default (Qwen/Bonsai stay Weak).
        return mode;
    }

    public static ModelHarnessMode ParseHarnessMode(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "strong" => ModelHarnessMode.Strong,
            "weak" => ModelHarnessMode.Weak,
            _ => ModelHarnessMode.Weak
        };

    private static bool IsOpenAiCompatibleBackend(string backend) =>
        backend is "openai"
            or "openai-compatible"
            or "custom"
            or "vllm"
            or "lmstudio"
            or "lm-studio"
            or "lm_studio"
            or "ollama"; // default gateway path uses OpenAI /v1 adapter

    private static bool LooksLikeFrontierName(string model) =>
        ContainsIgnore(model, "gpt-4", "gpt-5", "o1", "o3", "sonnet", "opus", "composer", "claude");

    private static int? TryParseBillions(string model)
    {
        var match = BillionsRegex().Match(model);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var n))
            return null;
        return n;
    }

    private static bool ContainsIgnore(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [GeneratedRegex("""(?i)(\d+)\s*b(?:-|\b|:|$)""", RegexOptions.CultureInvariant)]
    private static partial Regex BillionsRegex();
}
