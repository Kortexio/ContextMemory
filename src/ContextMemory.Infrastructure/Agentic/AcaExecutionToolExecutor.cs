using ContextMemory.Core.Agentic;
using System.Text.Json;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class AcaExecutionToolExecutor : IToolExecutor
{
    private readonly AcaDynamicSessionsClient _client;
    private readonly ILogger<AcaExecutionToolExecutor> _logger;

    public AcaExecutionToolExecutor(AcaDynamicSessionsClient client, ILogger<AcaExecutionToolExecutor> logger)
    {
        _client = client;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        TryResolveExecution(toolName, runtimeConfig) is not null;

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        var resolved = TryResolveExecution(toolCall.Function.Name, runtimeConfig);
        if (resolved is null)
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.ToolNotRegistered(toolCall.Function.Name, runtimeConfig),
                ExitCode = 1
            };
        }

        var (execution, runtime) = resolved.Value;
        if (execution.PoolEndpoint is not { Length: > 0 } endpoint)
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.AcaPoolNotConfigured(runtime, runtimeConfig),
                ExitCode = 1
            };
        }

        if (!AgenticNetworkEgressPolicy.IsAcaEndpointAllowed(runtimeConfig, execution, endpoint))
        {
            _logger.LogWarning(
                "ACA egress blocked for tenant {AppId} endpoint {Endpoint}",
                appId,
                endpoint);
            return AgenticNetworkEgressPolicy.BlockedResult(endpoint, runtimeConfig);
        }

        return runtime switch
        {
            "shell" => await ExecuteShellAsync(endpoint, toolCall.Function.Arguments, appId, runtimeConfig, cancellationToken)
                .ConfigureAwait(false),
            "python" => await ExecuteCodeAsync(endpoint, "python", toolCall.Function.Arguments, appId, runtimeConfig, cancellationToken)
                .ConfigureAwait(false),
            "node" => await ExecuteCodeAsync(endpoint, "node", toolCall.Function.Arguments, appId, runtimeConfig, cancellationToken)
                .ConfigureAwait(false),
            "custom" => await ExecuteContainerAsync(execution, endpoint, toolCall.Function.Arguments, appId, runtimeConfig, cancellationToken)
                .ConfigureAwait(false),
            _ => new ToolExecutionResult
            {
                Output = ToolExecutionMessages.UnsupportedAcaRuntime(runtime, runtimeConfig),
                ExitCode = 1
            }
        };
    }

    private async Task<ToolExecutionResult> ExecuteShellAsync(
        string endpoint,
        string argumentsJson,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var command = ExtractStringArgument(argumentsJson, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.MissingCommandParameter(runtimeConfig),
                ExitCode = 1
            };
        }

        return await _client.ExecuteAsync(
                endpoint,
                "shell",
                new AcaDynamicSessionsClient.ShellPayload { Command = command },
                appId,
                runtimeConfig.DefaultLanguage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ToolExecutionResult> ExecuteCodeAsync(
        string endpoint,
        string runtime,
        string argumentsJson,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var code = ExtractStringArgument(argumentsJson, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.MissingCodeParameter(runtimeConfig),
                ExitCode = 1
            };
        }

        return await _client.ExecuteAsync(
                endpoint,
                runtime,
                new AcaDynamicSessionsClient.CodePayload { Code = code },
                appId,
                runtimeConfig.DefaultLanguage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ToolExecutionResult> ExecuteContainerAsync(
        ExecutionToolConfig execution,
        string endpoint,
        string argumentsJson,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var command = ExtractStringArgument(argumentsJson, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.MissingCommandParameter(runtimeConfig),
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(execution.ContainerImage))
        {
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.MissingContainerImage(runtimeConfig),
                ExitCode = 1
            };
        }

        return await _client.ExecuteAsync(
                endpoint,
                "custom",
                new AcaDynamicSessionsClient.ContainerPayload
                {
                    ContainerImage = execution.ContainerImage,
                    Command = command
                },
                appId,
                runtimeConfig.DefaultLanguage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static (ExecutionToolConfig Execution, string Runtime)? TryResolveExecution(
        string toolName,
        AppRuntimeConfig runtimeConfig)
    {
        foreach (var execution in runtimeConfig.Agentic.Tools.Execution)
        {
            if (!string.Equals(execution.Type, "aca-session", StringComparison.OrdinalIgnoreCase))
                continue;

            var runtime = execution.Runtime.ToLowerInvariant();
            var expectedTool = runtime switch
            {
                "shell" => AgenticToolRegistry.ShellExecuteToolName,
                "python" => AgenticToolRegistry.PythonExecuteToolName,
                "node" => AgenticToolRegistry.NodeExecuteToolName,
                "custom" => AgenticToolRegistry.ContainerExecuteToolName,
                _ => null
            };

            if (expectedTool is not null
                && string.Equals(toolName, expectedTool, StringComparison.Ordinal))
                return (execution, runtime);
        }

        return null;
    }

    private static string ExtractStringArgument(string argumentsJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var value))
                return value.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return argumentsJson.Trim();
        }

        return string.Empty;
    }
}
