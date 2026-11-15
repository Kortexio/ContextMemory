using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticToolRegistryService : IAgenticToolRegistry
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IMcpToolCatalog _mcpCatalog;
    private readonly ICapabilityPolicyFilter _capabilityFilter;

    public AgenticToolRegistryService(
        IToolRegistry toolRegistry,
        IMcpToolCatalog mcpCatalog,
        ICapabilityPolicyFilter capabilityFilter)
    {
        _toolRegistry = toolRegistry;
        _mcpCatalog = mcpCatalog;
        _capabilityFilter = capabilityFilter;
    }

    public async Task<IReadOnlyList<OllamaTool>> BuildToolsAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var harness = ModelHarnessProfile.From(runtimeConfig);
        var tools = new List<OllamaTool>();

        // Built-ins: each IToolDescriptor owns its schemas — no hardcoded AddRange by family.
        tools.AddRange(_toolRegistry.BuildBuiltinSchemas(runtimeConfig));

        // Top-K MCP with real sanitized schemas (compacted on Weak).
        var mcpTools = await _mcpCatalog
            .GetToolsAsync(runtimeConfig, userQuery, recentToolNames, cancellationToken)
            .ConfigureAwait(false);
        foreach (var mcpTool in mcpTools)
        {
            var pinned = McpPinnedToolFactory.Create(mcpTool, runtimeConfig);
            var parameters = SchemaCompactor.Compact(pinned.Function.Parameters, harness.CompactSchemas);
            tools.Add(new OllamaTool(
                "function",
                new OllamaFunction(
                    pinned.Function.Name,
                    pinned.Function.Description,
                    parameters)));
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
