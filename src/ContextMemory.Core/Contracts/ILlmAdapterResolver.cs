namespace ContextMemory.Core.Contracts;

/// <summary>
/// Resolves the LLM adapter for a tenant backend name.
/// </summary>
public interface ILlmAdapterResolver
{
    ILlmAdapter Resolve(string llmBackend);
}
