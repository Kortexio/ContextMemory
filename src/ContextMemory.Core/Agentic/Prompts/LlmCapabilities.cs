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
    bool EnableProseToolCallPromotion,
    bool SanitizeSchemasAggressively,
    bool SupportsOpenAiJsonFormat,
    bool InlineEvidenceRules,
    bool PreferSkillDiscovery,
    int MaxMcpToolsHint,
    string? DefaultToolChoice);

public static partial class LlmCapabilitiesResolver
{
    public const int WeakMaxMcpToolsDefault = 24;

    public static LlmCapabilities From(AppRuntimeConfig config)
    {
        var profile = AgenticPromptProfileResolver.Resolve(config);
        var backend = (config.LlmBackend ?? "ollama").Trim().ToLowerInvariant();
        var openAiCompat = IsOpenAiCompatibleBackend(backend);
        var mode = ResolveHarnessMode(config, profile);

        var weak = mode == ModelHarnessMode.Weak;
        return new LlmCapabilities(
            HarnessMode: mode,
            PreferNativeToolCalls: !weak && profile is AgenticPromptProfile.OpenAi
                or AgenticPromptProfile.Claude
                or AgenticPromptProfile.ComposerLike,
            EnableProseToolCallPromotion: true,
            SanitizeSchemasAggressively: weak,
            SupportsOpenAiJsonFormat: openAiCompat
                || profile is AgenticPromptProfile.OpenAi or AgenticPromptProfile.ComposerLike,
            InlineEvidenceRules: weak,
            PreferSkillDiscovery: !weak,
            MaxMcpToolsHint: weak ? WeakMaxMcpToolsDefault : int.MaxValue,
            DefaultToolChoice: "auto");
    }

    /// <summary>
    /// Effective max MCP tools: min(tenant config, harness hint).
    /// </summary>
    public static int ResolveMaxMcpTools(AppRuntimeConfig config)
    {
        var configured = config.Agentic.Tools.MaxMcpToolsPerTurn > 0
            ? config.Agentic.Tools.MaxMcpToolsPerTurn
            : 12;
        var caps = From(config);
        return Math.Min(configured, caps.MaxMcpToolsHint);
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
