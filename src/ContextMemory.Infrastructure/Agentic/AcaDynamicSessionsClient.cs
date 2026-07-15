using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class AcaDynamicSessionsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AcaDynamicSessionsClient> _logger;

    public AcaDynamicSessionsClient(HttpClient httpClient, ILogger<AcaDynamicSessionsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string poolEndpoint,
        string runtime,
        object payload,
        string appId,
        string defaultLanguage = "en-US",
        CancellationToken cancellationToken = default)
    {
        if (poolEndpoint.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return ExecuteMock(runtime, payload);

        var executeUrl = poolEndpoint.TrimEnd('/') + "/execute";
        var bodyPayload = new Dictionary<string, object?>
        {
            ["runtime"] = runtime,
            ["tenantId"] = appId
        };

        if (payload is ShellPayload shell)
        {
            bodyPayload["command"] = shell.Command;
        }
        else if (payload is CodePayload code)
        {
            bodyPayload["code"] = code.Code;
        }
        else if (payload is ContainerPayload container)
        {
            bodyPayload["containerImage"] = container.ContainerImage;
            bodyPayload["command"] = container.Command;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, executeUrl)
            {
                Content = JsonContent.Create(bodyPayload, options: JsonOptions)
            };

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "ACA {Runtime} execution failed with HTTP {StatusCode} for tenant {AppId}",
                    runtime,
                    (int)response.StatusCode,
                    appId);
                return new ToolExecutionResult
                {
                    Output = $"HTTP {(int)response.StatusCode}: {body}",
                    ExitCode = (int)response.StatusCode
                };
            }

            var parsed = JsonSerializer.Deserialize<AcaExecuteResponse>(body, JsonOptions);
            return new ToolExecutionResult
            {
                Output = parsed?.Stdout ?? parsed?.Output ?? body,
                ExitCode = parsed?.ExitCode ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACA {Runtime} execution error for tenant {AppId}", runtime, appId);
            var langConfig = new AppRuntimeConfig { AppId = appId, DefaultLanguage = defaultLanguage };
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.AcaContactError(ex.Message, langConfig),
                ExitCode = 1
            };
        }
    }

    private static ToolExecutionResult ExecuteMock(string runtime, object payload)
    {
        var text = payload switch
        {
            ShellPayload shell => shell.Command,
            CodePayload code => code.Code,
            ContainerPayload container => $"{container.ContainerImage}:{container.Command}",
            _ => string.Empty
        };

        if (text.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolExecutionResult
            {
                Output = $"[mock:{runtime}] execution failed: {text}",
                ExitCode = 1
            };
        }

        return new ToolExecutionResult
        {
            Output = $"[mock:{runtime}] stdout: executed '{text}'\nexit_code: 0",
            ExitCode = 0
        };
    }

    public sealed class ShellPayload
    {
        public required string Command { get; init; }
    }

    public sealed class CodePayload
    {
        public required string Code { get; init; }
    }

    public sealed class ContainerPayload
    {
        public required string ContainerImage { get; init; }
        public required string Command { get; init; }
    }

    private sealed class AcaExecuteResponse
    {
        public string? Stdout { get; init; }
        public string? Output { get; init; }
        public int ExitCode { get; init; }
    }
}
