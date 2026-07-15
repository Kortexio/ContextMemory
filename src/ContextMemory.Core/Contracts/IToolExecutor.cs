using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Executes a single agentic tool call for a tenant runtime.
/// </summary>
public interface IToolExecutor
{
    bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig);

    Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);
}
