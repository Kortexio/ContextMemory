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
        _ = userQuery;
        var tools = new List<OllamaTool>();
        tools.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig, lazySchemas: true));

        var wikiTool = AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: true);
        if (wikiTool is not null)
            tools.Add(wikiTool);
        var wikiGrep = AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: true);
        if (wikiGrep is not null)
            tools.Add(wikiGrep);

        // Cursor-style discovery helpers (artifact/skill/log/tool_search/tool_describe).
        tools.AddRange(SessionDiscoveryTools.BuildTools(runtimeConfig));

        var caps = LlmCapabilitiesResolver.From(runtimeConfig);
        tools.AddRange(AgenticHttpTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticVisionTools.BuildTools(runtimeConfig, caps.SupportsVision));
        tools.AddRange(AgenticBrowserTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticDocumentTools.BuildTools(runtimeConfig));
        tools.AddRange(AgenticCanvasTools.BuildTools(runtimeConfig));

        // Lazy MCP: zero tools on first hop. Pin only MCP tools already invoked this conversation
        // (real schema from catalog). New MCP tools enter via tool_search → tool_describe → pin.
        if (recentToolNames is { Count: > 0 })
        {
            var recent = recentToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allMcp = await _mcpCatalog
                .GetAllToolsAsync(runtimeConfig, cancellationToken)
                .ConfigureAwait(false);
            var max = LlmCapabilitiesResolver.ResolveMaxMcpTools(runtimeConfig);
            foreach (var mcpTool in allMcp
                         .Where(t => recent.Contains(t.QualifiedName) || recent.Contains(t.Name))
                         .Take(max))
            {
                tools.Add(McpPinnedToolFactory.Create(mcpTool, runtimeConfig));
            }
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
        var names = tools
            .Select(t => t.Function.Name)
            .Where(n => !McpToolNaming.TryParseQualifiedName(n, out _, out _))
            .ToList();

        var mcpServers = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase) && i.Enabled)
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (mcpServers.Count == 0)
            return string.Join(", ", names);

        var mcpHint = $"MCP discovery helpers (servers: {string.Join(", ", mcpServers)})";
        return names.Count == 0 ? mcpHint : string.Join(", ", names) + "; " + mcpHint;
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
