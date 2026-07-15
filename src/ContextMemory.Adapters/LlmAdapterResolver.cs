using ContextMemory.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Adapters;

public sealed class LlmAdapterResolver : ILlmAdapterResolver
{
    private readonly IServiceProvider _serviceProvider;

    public LlmAdapterResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ILlmAdapter Resolve(string llmBackend)
    {
        return llmBackend.Trim().ToLowerInvariant() switch
        {
            "lmstudio" or "lm-studio" or "lm_studio" =>
                _serviceProvider.GetRequiredService<LmStudioAdapter>(),
            "openai" =>
                _serviceProvider.GetRequiredService<OpenAiAdapter>(),
            _ =>
                _serviceProvider.GetRequiredService<OllamaAdapter>()
        };
    }
}
