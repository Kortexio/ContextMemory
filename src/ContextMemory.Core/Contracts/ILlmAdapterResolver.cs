using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface ILlmAdapterResolver
{
    ILlmAdapter Resolve(string llmBackend);

    /// <summary>
    /// Resolves an adapter for the app, applying optional per-app endpoint/API key overrides.
    /// </summary>
    ILlmAdapter Resolve(AppRuntimeConfig runtimeConfig);
}
