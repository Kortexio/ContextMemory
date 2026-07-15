using ContextMemory.Core.Models;
using ContextMemory.Core.Session;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Builds the system prompt from persona, wiki, and web search context.
/// </summary>
public interface ISystemPromptBuilder
{
    string Build(
        string appId,
        AppRuntimeConfig config,
        SessionSnapshot snapshot,
        string? userQuery,
        string? webContextMarkdown = null);
}
