using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Adapters;

public sealed class LlmAdapterResolver : ILlmAdapterResolver
{
    private readonly IServiceProvider _serviceProvider;

    public LlmAdapterResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
        var kind = llmBackend.Trim().ToLowerInvariant();
        return kind switch
        {
            "lmstudio" or "lm-studio" or "lm_studio" =>
                _serviceProvider.GetRequiredService<LmStudioAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride),
            "openai" or "openai-compatible" or "custom" =>
                _serviceProvider.GetRequiredService<OpenAiAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride),
            _ =>
                _serviceProvider.GetRequiredService<OllamaAdapter>()
                    .WithConnection(endpointOverride, apiKeyOverride)
        };
    }
}
