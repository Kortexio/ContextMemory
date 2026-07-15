using ContextMemory.Core.Engine;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Post-processes LLM output: wiki queue, telemetry, and response shaping.
/// </summary>
public interface IChatPostProcessor
{
    Task<string?> PostProcessAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaMessage? lastUser,
        string assistantContent,
        AppRuntimeConfig runtimeConfig,
        ChatTurnContext turnContext,
        string? messageId,
        CancellationToken cancellationToken = default);
}
