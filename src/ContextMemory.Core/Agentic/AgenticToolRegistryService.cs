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
        CancellationToken cancellationToken = default)
    {
        var tools = new List<OllamaTool>();
        tools.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig));

        var mcpTools = await _mcpCatalog.GetToolsAsync(runtimeConfig, cancellationToken).ConfigureAwait(false);
        foreach (var mcpTool in mcpTools)
        {
            tools.Add(new OllamaTool(
                "function",
                new OllamaFunction(
                    mcpTool.QualifiedName,
                    AgenticToolDescriptionBuilder.BuildMcpDescription(mcpTool, runtimeConfig),
                    mcpTool.InputSchema ?? new { type = "object", properties = new { } })));
        }

        return tools;
    }

    public async Task<string> BuildToolNamesSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        var tools = await BuildToolsAsync(runtimeConfig, cancellationToken).ConfigureAwait(false);
        return string.Join(", ", tools.Select(t => t.Function.Name));
    }

    public List<OllamaMcpServer> BuildMcpServers(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Where(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Url))
            .Where(i => !i.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            .Select(i => new OllamaMcpServer(i.Name, i.Url))
            .ToList();
}
