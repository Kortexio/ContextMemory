using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Backend-agnostic LLM client (chat, generate, streaming, health).
/// </summary>
public interface ILlmAdapter
{
    Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OllamaResponse> ChatStreamAsync(OllamaRequest request, CancellationToken cancellationToken = default);
    Task<OllamaResponse> GenerateAsync(OllamaGenerateRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OllamaResponse> GenerateStreamAsync(
        OllamaGenerateRequest request,
        CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
