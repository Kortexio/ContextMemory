using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

/// <summary>
/// Protocol / harness knobs derived from the resolved prompt profile and LLM backend.
/// Distinct from guardrails (policy): these adapt wire format and repair behaviour to the model family.
/// </summary>
public sealed record LlmCapabilities(
    bool PreferNativeToolCalls,
    bool EnableProseToolCallPromotion,
    bool SanitizeSchemasAggressively,
    bool SupportsOpenAiJsonFormat,
    string? DefaultToolChoice);

public static class LlmCapabilitiesResolver
{
    public static LlmCapabilities From(AppRuntimeConfig config)
    {
        var profile = AgenticPromptProfileResolver.Resolve(config);
        var backend = (config.LlmBackend ?? "ollama").Trim().ToLowerInvariant();
        var openAiCompat = IsOpenAiCompatibleBackend(backend);

        return profile switch
        {
            AgenticPromptProfile.Qwen => new LlmCapabilities(
                PreferNativeToolCalls: false,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: true,
                SupportsOpenAiJsonFormat: openAiCompat,
                DefaultToolChoice: "auto"),

            AgenticPromptProfile.Ollama => new LlmCapabilities(
                PreferNativeToolCalls: false,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: true,
                SupportsOpenAiJsonFormat: openAiCompat,
                DefaultToolChoice: "auto"),

            AgenticPromptProfile.OpenAi => new LlmCapabilities(
                PreferNativeToolCalls: true,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: false,
                SupportsOpenAiJsonFormat: true,
                DefaultToolChoice: "auto"),

            AgenticPromptProfile.ComposerLike => new LlmCapabilities(
                PreferNativeToolCalls: true,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: false,
                SupportsOpenAiJsonFormat: true,
                DefaultToolChoice: "auto"),

            AgenticPromptProfile.Claude => new LlmCapabilities(
                PreferNativeToolCalls: true,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: false,
                SupportsOpenAiJsonFormat: openAiCompat,
                DefaultToolChoice: "auto"),

            _ => new LlmCapabilities(
                PreferNativeToolCalls: false,
                EnableProseToolCallPromotion: true,
                SanitizeSchemasAggressively: true,
                SupportsOpenAiJsonFormat: openAiCompat,
                DefaultToolChoice: "auto")
        };
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

    private static bool IsOpenAiCompatibleBackend(string backend) =>
        backend is "openai"
            or "openai-compatible"
            or "custom"
            or "vllm"
            or "lmstudio"
            or "lm-studio"
            or "lm_studio"
            or "ollama"; // default gateway path uses OpenAI /v1 adapter
}
