using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Main chat pipeline: enrichment, LLM call, post-processing, and session persistence.
/// </summary>
public interface IContextEngine
{
    Task<ChatPipelineResult> ProcessChatAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<OllamaResponse> ProcessChatStreamAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        CancellationToken cancellationToken = default);

    Task<ChatPipelineResult> FinalizeStreamAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        string assistantContent,
        string? messageId = null,
        CancellationToken cancellationToken = default);

    string? ReserveStreamMessageId(string appId, string userId, OllamaRequest request);

    Task PrimeStreamTurnAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        CancellationToken cancellationToken = default);
}
