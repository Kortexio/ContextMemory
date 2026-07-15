using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Core.Utilities;
using ContextMemory.Core.WebSearch;

namespace ContextMemory.Core.Engine;

public sealed class ChatRequestEnricher : IChatRequestEnricher
{
    private readonly ISessionStore _sessionStore;
    private readonly WebSearchEnricher _webSearchEnricher;
    private readonly ISystemPromptBuilder _systemPromptBuilder;

    public ChatRequestEnricher(
        ISessionStore sessionStore,
        WebSearchEnricher webSearchEnricher,
        ISystemPromptBuilder systemPromptBuilder)
    {
        _sessionStore = sessionStore;
        _webSearchEnricher = webSearchEnricher;
        _systemPromptBuilder = systemPromptBuilder;
    }

    public async Task<(OllamaRequest Request, OllamaMessage? LastUser, int PromptTokens)> EnrichAsync(
        string appId,
        string userId,
        string sessionId,
        OllamaRequest request,
        AppRuntimeConfig runtimeConfig,
        ChatTurnContext turnContext,
        CancellationToken cancellationToken = default)
    {
        var schema = string.IsNullOrWhiteSpace(runtimeConfig.WikiSchema)
            ? SessionDefaults.DefaultSchema
            : runtimeConfig.WikiSchema;

        await _sessionStore.EnsureInitializedAsync(appId, userId, sessionId, schema, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = await _sessionStore.LoadAsync(appId, userId, sessionId, cancellationToken).ConfigureAwait(false);
        var lastUser = request.GetLastUserMessage();

        var webEnrichment = await _webSearchEnricher
            .TryEnrichAsync(appId, lastUser?.Content, snapshot, runtimeConfig.WebSearch, runtimeConfig.DefaultLanguage, cancellationToken)
            .ConfigureAwait(false);
        turnContext.WebSearch = webEnrichment;

        var systemPrompt = _systemPromptBuilder.Build(
            appId, runtimeConfig, snapshot, lastUser?.Content, webEnrichment.PromptMarkdown);

        var messages = new List<OllamaMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(snapshot.Messages);

        if (lastUser is not null && !MessageAlreadyInHistory(snapshot.Messages, lastUser))
            messages.Add(lastUser);

        var model = string.IsNullOrWhiteSpace(request.Model) ? runtimeConfig.LlmModel : request.Model;
        var enriched = request with
        {
            Model = model,
            Messages = messages,
            Think = ShouldDisableThinking(model) ? false : request.Think
        };

        var prepared = (enriched, lastUser, TokenEstimator.Estimate(messages));
        turnContext.Prepared = prepared;
        return prepared;
    }

    private static bool MessageAlreadyInHistory(IReadOnlyList<OllamaMessage> history, OllamaMessage lastUser) =>
        history.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
        == lastUser.Content;

    private static bool ShouldDisableThinking(string model) =>
        model.Contains("qwen3", StringComparison.OrdinalIgnoreCase);
}
