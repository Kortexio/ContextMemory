using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Tool executor that needs session identity (artifacts, log search, skill bodies, subagents).
/// </summary>
public interface ISessionScopedToolExecutor
{
    bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig);

    Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report = null,
        CancellationToken cancellationToken = default);
}
