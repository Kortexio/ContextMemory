using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Engine;

public sealed class ChatPostProcessor : IChatPostProcessor
{
    private readonly ISessionStore _sessionStore;
    private readonly IMessageIdTracker _messageIdTracker;
    private readonly IWikiUpdateQueue _wikiUpdateQueue;

    public ChatPostProcessor(
        ISessionStore sessionStore,
        IMessageIdTracker messageIdTracker,
        IWikiUpdateQueue wikiUpdateQueue)
    {
        _sessionStore = sessionStore;
        _messageIdTracker = messageIdTracker;
        _wikiUpdateQueue = wikiUpdateQueue;
    }

    public async Task<string?> PostProcessAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaMessage? lastUser,
        string assistantContent,
        AppRuntimeConfig runtimeConfig,
        ChatTurnContext turnContext,
        string? messageId,
        CancellationToken cancellationToken = default)
    {
        if (lastUser is null)
            return null;

        var normalizedContent = OllamaLlmText.NormalizeAssistantContent(assistantContent);
        var toStore = new List<OllamaMessage>
        {
            lastUser,
            new() { Role = "assistant", Content = normalizedContent }
        };

        await _sessionStore.AppendMessagesAsync(
            appId, userId, sessionId, toStore, runtimeConfig.MaxHistoryMessages, cancellationToken).ConfigureAwait(false);

        messageId ??= _messageIdTracker.CreateAndTrack(appId, userId);

        await _wikiUpdateQueue.EnqueueAsync(
            new WikiUpdateJob(
                appId,
                userId,
                sessionId,
                lastUser.Content,
                normalizedContent,
                runtimeConfig,
                turnContext.WebSearch),
            cancellationToken).ConfigureAwait(false);

        return messageId;
    }
}
