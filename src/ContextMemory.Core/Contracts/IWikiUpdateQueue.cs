using ContextMemory.Core.Models;
using ContextMemory.Core.WebSearch;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Background queue for asynchronous session wiki maintenance.
/// </summary>
public interface IWikiUpdateQueue
{
    ValueTask EnqueueAsync(WikiUpdateJob job, CancellationToken cancellationToken = default);
}

public sealed record WikiUpdateJob(
    string AppId,
    string UserId,
    string SessionId,
    string UserMessage,
    string AssistantMessage,
    AppRuntimeConfig RuntimeConfig,
    WebSearchEnrichment? WebEnrichment);
