using ContextMemory.Core.Agentic.ToolProviders;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticToolRegistryService : IAgenticToolRegistry
{
    private readonly IReadOnlyList<IAgenticToolProvider> _providers;
    private readonly ICapabilityPolicyFilter _capabilityFilter;

    public AgenticToolRegistryService(
        IEnumerable<IAgenticToolProvider> providers,
        ICapabilityPolicyFilter capabilityFilter)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
        _capabilityFilter = capabilityFilter;
    }

    public async Task<IReadOnlyList<OllamaTool>> BuildToolsAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<OllamaTool>();
        foreach (var provider in _providers)
        {
            await provider
                .ContributeAsync(runtimeConfig, userQuery, recentToolNames, tools, cancellationToken)
                .ConfigureAwait(false);
        }

        var capability = PolicyLayersFactory
            .FromGuardrails(runtimeConfig.ResolvedPolicy, runtimeConfig.Agentic.Guardrails)
            .Capability;
        return _capabilityFilter.FilterTools(tools, capability);
    }

    public async Task<string> BuildToolNamesSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var tools = await BuildToolsAsync(runtimeConfig, userQuery, recentToolNames, cancellationToken)
            .ConfigureAwait(false);
        return ClientSideToolCalling.FormatToolNamesSummary(tools);
    }

    public List<OllamaMcpServer> BuildMcpServers(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => i.Enabled)
            .Where(i => i.IsConfigured)
            .Where(i => i.IsHttpTransport)
            .Where(i => !i.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            .Select(i => new OllamaMcpServer(i.Name, i.Url))
            .ToList();
}
