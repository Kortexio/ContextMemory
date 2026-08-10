using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class BrowserToolsExecutor : ISessionScopedToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionArtifactStore _artifacts;
    private readonly ILogger<BrowserToolsExecutor> _logger;

    public BrowserToolsExecutor(
        IHttpClientFactory httpClientFactory,
        ISessionArtifactStore artifacts,
        ILogger<BrowserToolsExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Browser.Enabled
        && AgenticBrowserTools.IsBrowserTool(toolName);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(toolCall.Function.Name, runtimeConfig))
        {
            return new ToolExecutionResult
            {
                Output = "Browser tools are disabled for this tenant.",
                ExitCode = 1
            };
        }

        var browser = runtimeConfig.Agentic.Tools.Browser;
        var endpoint = ResolveEndpoint(runtimeConfig, browser);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new ToolExecutionResult
            {
                Output = "No sandbox endpoint configured for browser tools.",
                ExitCode = 1
            };
        }

        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            args = doc.RootElement.Clone();
        }
        catch
        {
            return new ToolExecutionResult { Output = "Invalid browser tool JSON arguments.", ExitCode = 1 };
        }

        if (string.Equals(toolCall.Function.Name, AgenticBrowserTools.Navigate, StringComparison.OrdinalIgnoreCase))
        {
            var url = args.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                return new ToolExecutionResult { Output = "browser_navigate requires url.", ExitCode = 1 };

            var hosts = browser.AllowedHosts.Count > 0
                ? browser.AllowedHosts
                : runtimeConfig.Agentic.Tools.Http.AllowedHosts;
            if (!HostAllowlist.TryValidatePublicHttpUrl(url, hosts, out _, out var error))
                return new ToolExecutionResult { Output = error, ExitCode = 403 };
        }

        var action = toolCall.Function.Name switch
        {
            _ when string.Equals(toolCall.Function.Name, AgenticBrowserTools.Navigate, StringComparison.OrdinalIgnoreCase)
                => "navigate",
            _ when string.Equals(toolCall.Function.Name, AgenticBrowserTools.Snapshot, StringComparison.OrdinalIgnoreCase)
                => "snapshot",
            _ when string.Equals(toolCall.Function.Name, AgenticBrowserTools.Click, StringComparison.OrdinalIgnoreCase)
                => "click",
            _ when string.Equals(toolCall.Function.Name, AgenticBrowserTools.Type, StringComparison.OrdinalIgnoreCase)
                => "type",
            _ when string.Equals(toolCall.Function.Name, AgenticBrowserTools.Screenshot, StringComparison.OrdinalIgnoreCase)
                => "screenshot",
            _ => null
        };

        if (action is null)
            return new ToolExecutionResult { Output = "Unknown browser action.", ExitCode = 1 };

        var client = _httpClientFactory.CreateClient(HttpToolsExecutor.HttpClientName);
        var timeout = TimeSpan.FromSeconds(browser.TimeoutSeconds > 0 ? browser.TimeoutSeconds : 60);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var payload = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["sessionKey"] = $"{appId}:{sessionId}",
            ["headless"] = browser.Headless,
            ["args"] = JsonSerializer.Deserialize<object>(args.GetRawText())
        };

        try
        {
            var url = endpoint.TrimEnd('/') + "/browser";
            using var response = await client
                .PostAsJsonAsync(url, payload, cts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ToolExecutionResult
                {
                    Output = $"Browser runtime HTTP {(int)response.StatusCode}: {body}",
                    ExitCode = 1
                };
            }

            using var resultDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = resultDoc.RootElement;
            var output = root.TryGetProperty("output", out var o) ? o.GetString() ?? body : body;
            var exit = root.TryGetProperty("exitCode", out var e) && e.TryGetInt32(out var code) ? code : 0;

            Dictionary<string, string>? entities = null;
            if (root.TryGetProperty("screenshotBase64", out var shot)
                && shot.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(shot.GetString()))
            {
                var b64 = shot.GetString()!;
                var artifactId = $"screenshot:{Guid.NewGuid():N}"[..24];
                await _artifacts
                    .WriteAsync(appId, userId, sessionId, artifactId, b64, cancellationToken)
                    .ConfigureAwait(false);
                entities = new Dictionary<string, string>
                {
                    ["artifactId"] = artifactId,
                    [AgenticVisionTools.ImageBase64Entity] = b64
                };
                output = (output ?? string.Empty).TrimEnd()
                    + $"\n\nScreenshot stored as artifactId={artifactId}."
                    + (LlmCapabilitiesGate.SupportsVision(runtimeConfig)
                        ? " Call screenshot_describe or rely on auto-attach for vision models."
                        : " Model has no vision — describe from accessibility snapshot instead.");
            }

            return new ToolExecutionResult
            {
                Output = output ?? string.Empty,
                ExitCode = exit,
                Entities = entities
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Browser tool {Tool} failed", toolCall.Function.Name);
            return new ToolExecutionResult
            {
                Output = $"Browser tool failed: {ex.Message}",
                ExitCode = 1
            };
        }
    }

    private static string? ResolveEndpoint(AppRuntimeConfig runtimeConfig, AgenticBrowserToolsConfig browser)
    {
        if (!string.IsNullOrWhiteSpace(browser.SandboxEndpoint))
            return browser.SandboxEndpoint;

        var exec = runtimeConfig.Agentic.Tools.Execution
            .FirstOrDefault(e =>
                string.Equals(e.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(e.SandboxEndpoint));
        return exec?.SandboxEndpoint;
    }
}
