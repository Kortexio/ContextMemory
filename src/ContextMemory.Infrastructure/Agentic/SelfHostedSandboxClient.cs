using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class SelfHostedSandboxClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SelfHostedSandboxClient> _logger;

    public SelfHostedSandboxClient(HttpClient httpClient, ILogger<SelfHostedSandboxClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string sandboxEndpoint,
        string runtime,
        object payload,
        string appId,
        string defaultLanguage = "en-US",
        CancellationToken cancellationToken = default)
    {
        if (sandboxEndpoint.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return ExecuteMock(runtime, payload);

        var executeUrl = sandboxEndpoint.TrimEnd('/') + "/execute";
        var bodyPayload = new Dictionary<string, object?>
        {
            ["runtime"] = runtime,
            ["tenantId"] = appId
        };

        if (payload is AcaDynamicSessionsClient.ShellPayload shell)
            bodyPayload["command"] = shell.Command;
        else if (payload is AcaDynamicSessionsClient.CodePayload code)
            bodyPayload["code"] = code.Code;

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
                    "Sandbox {Runtime} execution failed with HTTP {StatusCode} for tenant {AppId}",
                    runtime,
                    (int)response.StatusCode,
                    appId);
                return new ToolExecutionResult
                {
                    Output = $"HTTP {(int)response.StatusCode}: {body}",
                    ExitCode = (int)response.StatusCode
                };
            }

            var parsed = JsonSerializer.Deserialize<SandboxExecuteResponse>(body, JsonOptions);
            return new ToolExecutionResult
            {
                Output = parsed?.Output ?? body,
                ExitCode = parsed?.ExitCode ?? 0
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Sandbox execution request failed for tenant {AppId}", appId);
            var langConfig = new AppRuntimeConfig { AppId = appId, DefaultLanguage = defaultLanguage };
            return new ToolExecutionResult
            {
                Output = ToolExecutionMessages.SandboxContactError(ex.Message, langConfig),
                ExitCode = 1
            };
        }
    }

    private static ToolExecutionResult ExecuteMock(string runtime, object payload)
    {
        var summary = runtime switch
        {
            "shell" when payload is AcaDynamicSessionsClient.ShellPayload shell => shell.Command,
            "python" or "node" when payload is AcaDynamicSessionsClient.CodePayload code => code.Code,
            _ => "(payload)"
        };

        return new ToolExecutionResult
        {
            Output = $"[mock sandbox/{runtime}] ok: {summary}",
            ExitCode = 0
        };
    }

    private sealed class SandboxExecuteResponse
    {
        public string? Output { get; init; }
        public int ExitCode { get; init; }
    }
}
