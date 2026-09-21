using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.ToolProviders;

public sealed class ExecutionToolProvider : IAgenticToolProvider
{
    public int Order => 10;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(AgenticToolRegistry.BuildExecutionTools(runtimeConfig, lazySchemas: false));
        return ValueTask.CompletedTask;
    }
}

public sealed class WikiToolProvider : IAgenticToolProvider
{
    public int Order => 20;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        var wikiTool = AgenticToolRegistry.BuildWikiSearchTool(runtimeConfig, lazySchemas: false);
        if (wikiTool is not null)
            tools.Add(wikiTool);
        var wikiGrep = AgenticToolRegistry.BuildWikiGrepTool(runtimeConfig, lazySchemas: false);
        if (wikiGrep is not null)
            tools.Add(wikiGrep);
        return ValueTask.CompletedTask;
    }
}

public sealed class DiscoveryToolProvider : IAgenticToolProvider
{
    public int Order => 30;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(SessionDiscoveryTools.BuildTools(runtimeConfig));
        return ValueTask.CompletedTask;
    }
}

public sealed class HttpToolProvider : IAgenticToolProvider
{
    public int Order => 40;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(AgenticHttpTools.BuildTools(runtimeConfig));
        return ValueTask.CompletedTask;
    }
}

public sealed class VisionToolProvider : IAgenticToolProvider
{
    public int Order => 50;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        var caps = LlmCapabilitiesResolver.From(runtimeConfig);
        tools.AddRange(AgenticVisionTools.BuildTools(runtimeConfig, caps.SupportsVision));
        return ValueTask.CompletedTask;
    }
}

public sealed class BrowserToolProvider : IAgenticToolProvider
{
    public int Order => 60;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(AgenticBrowserTools.BuildTools(runtimeConfig));
        return ValueTask.CompletedTask;
    }
}

public sealed class DocumentToolProvider : IAgenticToolProvider
{
    public int Order => 70;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(AgenticDocumentTools.BuildTools(runtimeConfig));
        return ValueTask.CompletedTask;
    }
}

public sealed class CanvasToolProvider : IAgenticToolProvider
{
    public int Order => 80;

    public ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        tools.AddRange(AgenticCanvasTools.BuildTools(runtimeConfig));
        return ValueTask.CompletedTask;
    }
}

public sealed class McpToolProvider : IAgenticToolProvider
{
    private readonly IMcpToolCatalog _mcpCatalog;

    public McpToolProvider(IMcpToolCatalog mcpCatalog) => _mcpCatalog = mcpCatalog;

    public int Order => 90;

    public async ValueTask ContributeAsync(
        AppRuntimeConfig runtimeConfig,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames,
        List<OllamaTool> tools,
        CancellationToken cancellationToken)
    {
        object openParameters = McpPinnedToolFactory.OpenStubParameters();
        var mcpTools = await _mcpCatalog
            .GetToolsAsync(runtimeConfig, userQuery, recentToolNames, cancellationToken)
            .ConfigureAwait(false);
        foreach (var mcpTool in mcpTools)
        {
            var fullDescription = AgenticToolDescriptionBuilder.BuildMcpDescription(mcpTool, runtimeConfig);
            tools.Add(new OllamaTool(
                "function",
                new OllamaFunction(
                    mcpTool.QualifiedName,
                    SessionDiscoveryTools.ShortenDescription(fullDescription),
                    openParameters)));
        }
    }
}
