using ContextMemory.Core.Contracts;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class LlmAdapterResolver : ILlmAdapterResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ContextMemoryOptions _options;

    public LlmAdapterResolver(IServiceProvider serviceProvider, IOptions<ContextMemoryOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public ILlmAdapter Resolve(string llmBackend) =>
        Resolve(llmBackend, endpointOverride: null, apiKeyOverride: null);

    public ILlmAdapter Resolve(AppRuntimeConfig runtimeConfig) =>
        Resolve(
            runtimeConfig.LlmBackend,
            string.IsNullOrWhiteSpace(runtimeConfig.LlmEndpoint) ? null : runtimeConfig.LlmEndpoint,
            string.IsNullOrWhiteSpace(runtimeConfig.LlmApiKey) ? null : runtimeConfig.LlmApiKey);

    private ILlmAdapter Resolve(string llmBackend, string? endpointOverride, string? apiKeyOverride)
    {
        var kind = (llmBackend ?? "ollama").Trim().ToLowerInvariant();
        return kind switch
        {
            // Native Ollama /api/chat — only when /v1 is unavailable.
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
