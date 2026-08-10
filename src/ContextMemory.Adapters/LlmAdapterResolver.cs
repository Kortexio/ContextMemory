using ContextMemory.Core.Contracts;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class LlmAdapterResolver : ILlmAdapterResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<LlmAdapterResolver> _logger;

    public LlmAdapterResolver(
        IServiceProvider serviceProvider,
        IOptions<ContextMemoryOptions> options,
        ILogger<LlmAdapterResolver> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public ILlmAdapter Resolve(string llmBackend) =>
        Resolve(llmBackend, endpointOverride: null, apiKeyOverride: null, preferNativeForNumCtx: false);

    public ILlmAdapter Resolve(AppRuntimeConfig runtimeConfig)
    {
        var backend = runtimeConfig.LlmBackend;
        var preferNative = ShouldPreferOllamaNativeForNumCtx(runtimeConfig);
        if (preferNative
            && string.Equals(backend?.Trim(), "ollama", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Using ollama-native (/api/chat) because llmOptions.numCtx={NumCtx}; Ollama /v1 ignores options.num_ctx",
                runtimeConfig.LlmOptions?.NumCtx);
            backend = "ollama-native";
        }

        return Resolve(
            backend ?? "ollama-native",
            string.IsNullOrWhiteSpace(runtimeConfig.LlmEndpoint) ? null : runtimeConfig.LlmEndpoint,
            string.IsNullOrWhiteSpace(runtimeConfig.LlmApiKey) ? null : runtimeConfig.LlmApiKey,
            preferNativeForNumCtx: false);
    }

    /// <summary>
    /// Ollama OpenAI-compat <c>/v1</c> silently ignores <c>options.num_ctx</c>.
    /// When the tenant sets <see cref="LlmGenerationConfig.NumCtx"/>, prefer native <c>/api/chat</c>.
    /// </summary>
    public static bool ShouldPreferOllamaNativeForNumCtx(AppRuntimeConfig runtimeConfig)
    {
        var backend = (runtimeConfig.LlmBackend ?? "ollama").Trim();
        if (!backend.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            return false;

        return runtimeConfig.LlmOptions?.NumCtx is > 0;
    }

    private ILlmAdapter Resolve(
        string llmBackend,
        string? endpointOverride,
        string? apiKeyOverride,
        bool preferNativeForNumCtx)
    {
        _ = preferNativeForNumCtx;
        var kind = (llmBackend ?? "ollama").Trim().ToLowerInvariant();
        return kind switch
        {
            // Native Ollama /api/chat — respects options.num_ctx (unlike /v1).
            "ollama-native" =>
                _serviceProvider.GetRequiredService<OllamaAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride),

            "lmstudio" or "lm-studio" or "lm_studio" =>
                _serviceProvider.GetRequiredService<LmStudioAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride),

            // Default: OpenAI-compatible /v1 (Ollama /v1, vLLM, OpenAI, Azure-compatible, custom).
            "ollama" =>
                _serviceProvider.GetRequiredService<OpenAiAdapter>()
                    .WithConnection(endpointOverride ?? _options.OllamaEndpoint, apiKeyOverride),

            "vllm" or "openai" or "openai-compatible" or "custom" =>
                _serviceProvider.GetRequiredService<OpenAiAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride),

            _ =>
                _serviceProvider.GetRequiredService<OpenAiAdapter>()
                    .WithConnection(endpointOverride ?? _options.OllamaEndpoint, apiKeyOverride)
        };
    }
}
