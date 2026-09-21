using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.ToolProviders;

/// <summary>
/// Contributes a slice of Ollama tools to the turn catalog.
/// Implementations are ordered and composed by <see cref="AgenticToolRegistryService"/>.
/// </summary>
public interface IAgenticToolProvider
{
    int Order { get; }

    ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken);
}
