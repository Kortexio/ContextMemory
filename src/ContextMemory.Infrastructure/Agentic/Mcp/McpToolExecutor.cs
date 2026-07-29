using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

public sealed class McpToolExecutor : IToolExecutor
{
    private readonly McpJsonRpcClient _client;
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(McpJsonRpcClient client, ILogger<McpToolExecutor> logger)
    {
        _client = client;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig)
    {
        if (!McpToolNaming.TryParseQualifiedName(toolName, out var serverName, out _))
            return false;

        return runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && string.Equals(McpToolNaming.SanitizeForCompare(i.Name), serverName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        if (!McpToolNaming.TryParseQualifiedName(toolCall.Function.Name, out var serverName, out var mcpToolName))
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.InvalidMcpToolName(toolCall.Function.Name, runtimeConfig),
                ExitCode = 1
            };
        }

        var server = runtimeConfig.Agentic.Tools.Integrations.FirstOrDefault(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(McpToolNaming.SanitizeForCompare(i.Name), serverName, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.McpServerNotConfigured(serverName, runtimeConfig),
                ExitCode = 1
            };
        }

        if (!server.Enabled)
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.McpServerNotConfigured(serverName, runtimeConfig),
                ExitCode = 1
            };
        }

        if (!AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(runtimeConfig, server))
        {
            _logger.LogWarning(
                "MCP egress blocked for server {Server} ({Url}) app {AppId}",
                server.Name,
                server.Url,
                appId);
            return AgenticNetworkEgressPolicy.BlockedResult(server.Url, runtimeConfig);
        }

        if (server.ToolAllowlist.Count > 0
            && !server.ToolAllowlist.Any(t => string.Equals(t, mcpToolName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ToolExecutionResult
            {
                Output = $"MCP tool `{mcpToolName}` is not allowed on server `{server.Name}`.",
                ExitCode = 1
            };
        }

        if (server.ToolDenylist.Any(t => string.Equals(t, mcpToolName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ToolExecutionResult
            {
                Output = $"MCP tool `{mcpToolName}` is blocked on server `{server.Name}`.",
                ExitCode = 1
            };
        }

        try
        {
            var output = await _client
                .CallToolAsync(appId, server, mcpToolName, toolCall.Function.Arguments, cancellationToken)
                .ConfigureAwait(false);

            return new ToolExecutionResult
            {
                Output = output.Raw,
                Summary = output.Summary,
                Entities = output.Entities,
                OutputTruncated = output.Truncated,
                ExitCode = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP tool {Tool} on server {Server} failed for app {AppId}", mcpToolName, serverName, appId);
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.McpError(serverName, mcpToolName, ex.Message, runtimeConfig),
                ExitCode = 1
            };
        }
    }
}
