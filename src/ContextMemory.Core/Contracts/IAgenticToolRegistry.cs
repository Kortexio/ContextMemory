using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

/// <summary>
/// Registers tool schemas exposed to the LLM for agentic tenants.
/// </summary>
public interface IAgenticToolRegistry
{
    Task<IReadOnlyList<OllamaTool>> BuildToolsAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);

    Task<string> BuildToolNamesSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default);

    List<OllamaMcpServer> BuildMcpServers(AppRuntimeConfig runtimeConfig);
}
