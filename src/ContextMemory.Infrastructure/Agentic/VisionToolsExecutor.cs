using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class VisionToolsExecutor : ISessionScopedToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionArtifactStore _artifacts;
    private readonly ILogger<VisionToolsExecutor> _logger;

    public VisionToolsExecutor(
        IHttpClientFactory httpClientFactory,
        ISessionArtifactStore artifacts,
        ILogger<VisionToolsExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig)
    {
        if (!runtimeConfig.Agentic.Tools.Vision.Enabled || !AgenticVisionTools.IsVisionTool(toolName))
            return false;

        return LlmCapabilitiesGate.SupportsVision(runtimeConfig);
    }

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
                Output = "Vision tools require tools.vision.enabled and a vision-capable model (or forceEnable).",
                ExitCode = 1
            };
        }

        string? url = null;
        string? artifactId = null;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            if (root.TryGetProperty("url", out var u))
                url = u.GetString();
            if (root.TryGetProperty("artifactId", out var a))
                artifactId = a.GetString();
        }
        catch
        {
            return Fail("Invalid vision tool arguments.");
        }

        string? base64 = null;

        if (!string.IsNullOrWhiteSpace(artifactId))
        {
            var content = await _artifacts
                .ReadAsync(appId, userId, sessionId, artifactId.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
                return Fail($"Artifact '{artifactId}' not found.");

            base64 = ExtractBase64(content);
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                base64 = OpenAiImageHelper.StripDataUrl(url);
            }
            else
            {
                var hosts = runtimeConfig.Agentic.Tools.Vision.AllowedHosts;
                if (hosts.Count == 0)
                    hosts = runtimeConfig.Agentic.Tools.Http.AllowedHosts;

                if (!HostAllowlist.TryValidatePublicHttpUrl(url, hosts, out var uri, out var error))
                    return new ToolExecutionResult { Output = error, ExitCode = 403 };

                try
                {
                    var client = _httpClientFactory.CreateClient(HttpToolsExecutor.HttpClientName);
                    using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        return Fail($"Failed to download image: HTTP {(int)response.StatusCode}");

                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    if (bytes.Length > 8_000_000)
                        return Fail("Image exceeds 8MB limit.");
                    base64 = Convert.ToBase64String(bytes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "read_image download failed");
                    return Fail($"Image download failed: {ex.Message}");
                }
            }
        }
        else
        {
            return Fail("Provide url or artifactId.");
        }

        if (string.IsNullOrWhiteSpace(base64))
            return Fail("Could not decode image bytes.");

        var storeId = $"vision:{Guid.NewGuid():N}"[..20];
        await _artifacts.WriteAsync(appId, userId, sessionId, storeId, base64, cancellationToken)
            .ConfigureAwait(false);

        return new ToolExecutionResult
        {
            Output =
                $"Image ready for multimodal turn (artifactId={storeId}, bytes≈{base64.Length * 3 / 4}). "
                + "The gateway will attach it to the next model call.",
            ExitCode = 0,
            Entities = new Dictionary<string, string>
            {
                [AgenticVisionTools.ImageBase64Entity] = base64,
                ["artifactId"] = storeId
            }
        };
    }

    private static string? ExtractBase64(string content)
    {
        content = content.Trim();
        if (content.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return OpenAiImageHelper.StripDataUrl(content);

        // PNG/JPEG raw base64 usually starts without whitespace
        var sb = new StringBuilder(content.Length);
        foreach (var ch in content)
        {
            if (!char.IsWhiteSpace(ch))
                sb.Append(ch);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static ToolExecutionResult Fail(string message) =>
        new() { Output = message, ExitCode = 1 };
}

/// <summary>Avoid circular project refs — tiny helper mirroring OpenAiProtocolMapper normalize.</summary>
internal static class OpenAiImageHelper
{
    public static string? StripDataUrl(string raw)
    {
        var comma = raw.IndexOf(',');
        if (comma < 0 || comma == raw.Length - 1)
            return null;
        return raw[(comma + 1)..].Trim();
    }
}

internal static class LlmCapabilitiesGate
{
    public static bool SupportsVision(AppRuntimeConfig config) =>
        Core.Agentic.Prompts.LlmCapabilitiesResolver.ResolveSupportsVision(config);
}
