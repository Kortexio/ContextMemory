using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticToolRegistryService : IAgenticToolRegistry
{
    private readonly IMcpToolCatalog _mcpCatalog;
    private readonly ICapabilityPolicyFilter _capabilityFilter;

    public AgenticToolRegistryService(
        IMcpToolCatalog mcpCatalog,
        ICapabilityPolicyFilter capabilityFilter)
    {
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
        // Full schemas by default (CodeForge-style) — lazy stubs burned weak-model turns on {}.
        tools.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig, lazySchemas: false));

        var wikiTool = AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: false);
        if (wikiTool is not null)
            tools.Add(wikiTool);
        var wikiGrep = AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: false);
        if (wikiGrep is not null)
            tools.Add(wikiGrep);

        // Discovery helpers — tool_describe remains for tools outside the top-K / still-unclear schemas.
        tools.AddRange(SessionDiscoveryTools.BuildTools(runtimeConfig));

        var caps = LlmCapabilitiesResolver.From(runtimeConfig);
        tools.AddRange(AgenticHttpTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticVisionTools.BuildTools(runtimeConfig, caps.SupportsVision));
        tools.AddRange(AgenticBrowserTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticDocumentTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticCanvasTools.BuildTools(runtimeConfig));

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
