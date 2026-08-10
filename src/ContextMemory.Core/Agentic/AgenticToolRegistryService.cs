using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class AgenticToolRegistryService : IAgenticToolRegistry
{
    private readonly IMcpToolCatalog _mcpCatalog;

    public AgenticToolRegistryService(IMcpToolCatalog mcpCatalog)
    {
        _mcpCatalog = mcpCatalog;
    }

    public async Task<IReadOnlyList<OllamaTool>> BuildToolsAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<OllamaTool>();
        tools.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig, lazySchemas: true));

        var wikiTool = AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: true);
        if (wikiTool is not null)
            tools.Add(wikiTool);
        var wikiGrep = AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: true);
        if (wikiGrep is not null)
            tools.Add(wikiGrep);

        // Cursor-style discovery helpers (artifact/skill/log/tool_describe).
        tools.AddRange(SessionDiscoveryTools.BuildTools(runtimeConfig));

        object openParameters = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
            ["additionalProperties"] = true
        };

        var mcpTools = await _mcpCatalog
            .GetToolsAsync(runtimeConfig, userQuery, recentToolNames, cancellationToken)
            .ConfigureAwait(false);
        foreach (var mcpTool in mcpTools)
        {
            var fullDescription = AgenticToolDescriptionBuilder.BuildMcpDescription(mcpTool, runtimeConfig);
            // Name-only style: open schema; full schema via tool_describe.
            tools.Add(new OllamaTool(
                "function",
                new OllamaFunction(
                    mcpTool.QualifiedName,
                    SessionDiscoveryTools.ShortenDescription(fullDescription),
                    openParameters)));
        }

        return tools;
    }

    public async Task<string> BuildToolNamesSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery = null,
        IReadOnlyList<string>? recentToolNames = null,
        CancellationToken cancellationToken = default)
    {
        var tools = await BuildToolsAsync(runtimeConfig, userQuery, recentToolNames, cancellationToken).ConfigureAwait(false);
        return string.Join(", ", tools.Select(t => t.Function.Name));
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
