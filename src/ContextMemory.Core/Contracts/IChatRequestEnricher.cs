using ContextMemory.Core.Engine;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Enriches chat requests with history, wiki, and web search before the LLM call.
/// </summary>
public interface IChatRequestEnricher
{
    Task<(OllamaRequest Request, OllamaMessage? LastUser, int PromptTokens)> EnrichAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        AppRuntimeConfig runtimeConfig,
        ChatTurnContext turnContext,
        CancellationToken cancellationToken = default);
}
