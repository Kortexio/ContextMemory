using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.WebSearch;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class HttpToolsExecutor : IToolExecutor
{
    public const string HttpClientName = "HttpTools";

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebSearchProviderResolver _webSearchResolver;
    private readonly ILogger<HttpToolsExecutor> _logger;

    public HttpToolsExecutor(
        IHttpClientFactory httpClientFactory,
        IWebSearchProviderResolver webSearchResolver,
        ILogger<HttpToolsExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _webSearchResolver = webSearchResolver;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Http.Enabled
        && AgenticHttpTools.IsHttpTool(toolName)
        && (string.Equals(toolName, AgenticHttpTools.WebSearch, StringComparison.OrdinalIgnoreCase)
            ? runtimeConfig.Agentic.Tools.Http.AllowWebSearchTool
            : true)
        && (string.Equals(toolName, AgenticHttpTools.HttpRequest, StringComparison.OrdinalIgnoreCase)
            ? runtimeConfig.Agentic.Tools.Http.AllowHttpRequest
            : true);

    public Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(toolCall.Function.Name, runtimeConfig))
        {
            return Task.FromResult(new ToolExecutionResult
            {
                Output = $"{toolCall.Function.Name} is not enabled for this tenant.",
                ExitCode = 1
            });
        }

        return toolCall.Function.Name.ToLowerInvariant() switch
        {
            AgenticHttpTools.WebSearch => ExecuteWebSearchAsync(toolCall, runtimeConfig, cancellationToken),
            AgenticHttpTools.FetchUrl => ExecuteFetchAsync(toolCall, runtimeConfig, method: "GET", cancellationToken),
            AgenticHttpTools.HttpRequest => ExecuteHttpRequestAsync(toolCall, runtimeConfig, cancellationToken),
            _ => Task.FromResult(new ToolExecutionResult { Output = "Unknown HTTP tool.", ExitCode = 1 })
        };
    }

    private async Task<ToolExecutionResult> ExecuteWebSearchAsync(
        OllamaToolCall toolCall,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        string query;
        var maxResults = runtimeConfig.WebSearch.MaxResults > 0 ? runtimeConfig.WebSearch.MaxResults : 5;
        try
        {
            using var doc = JsonDocument.Parse(Args(toolCall));
            var root = doc.RootElement;
            query = root.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("maxResults", out var mr) && mr.TryGetInt32(out var n) && n > 0)
                maxResults = Math.Clamp(n, 1, 20);
        }
        catch
        {
            return Fail("Invalid web_search arguments. Expected JSON with \"query\".");
        }

        if (string.IsNullOrWhiteSpace(query))
            return Fail("web_search requires a non-empty query.");

        if (!_webSearchResolver.TryResolve(runtimeConfig.WebSearch.Provider, out var provider) || provider is null)
        {
            return Fail(
                $"No web search provider resolved for '{runtimeConfig.WebSearch.Provider}'. "
                + "Configure WebSearch.provider (tavily/brave) and API keys on the host.");
        }

        try
        {
            var result = await provider
                .SearchAsync(new WebSearchRequest(query.Trim(), maxResults), cancellationToken)
                .ConfigureAwait(false);
            var markdown = WebSearchFormatter.ToMarkdown(
                result,
                runtimeConfig.WebSearch.MaxContextChars > 0 ? runtimeConfig.WebSearch.MaxContextChars : 12_000,
                query,
                runtimeConfig.DefaultLanguage);
            if (string.IsNullOrWhiteSpace(markdown))
                return new ToolExecutionResult { Output = "No web search hits.", ExitCode = 0 };

            return new ToolExecutionResult { Output = markdown, ExitCode = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "web_search failed for provider {Provider}", provider.ProviderName);
            return Fail($"web_search failed: {ex.Message}");
        }
    }

    private async Task<ToolExecutionResult> ExecuteHttpRequestAsync(
        OllamaToolCall toolCall,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        string method;
        try
        {
            using var doc = JsonDocument.Parse(Args(toolCall));
            method = doc.RootElement.TryGetProperty("method", out var m)
                ? m.GetString() ?? "GET"
                : "GET";
        }
        catch
        {
            return Fail("Invalid http_request arguments.");
        }

        return await ExecuteFetchAsync(toolCall, runtimeConfig, method, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolExecutionResult> ExecuteFetchAsync(
        OllamaToolCall toolCall,
        AppRuntimeConfig runtimeConfig,
        string method,
        CancellationToken cancellationToken)
    {
        var httpCfg = runtimeConfig.Agentic.Tools.Http;
        string url;
        string? body = null;
        Dictionary<string, string>? headers = null;
        var maxChars = httpCfg.MaxResponseChars > 0 ? httpCfg.MaxResponseChars : 50_000;

        try
        {
            using var doc = JsonDocument.Parse(Args(toolCall));
            var root = doc.RootElement;
            url = root.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String)
                body = b.GetString();
            if (root.TryGetProperty("maxChars", out var mc) && mc.TryGetInt32(out var n) && n > 0)
                maxChars = Math.Min(n, maxChars);
            if (root.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in h.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        headers[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            return Fail("Invalid HTTP tool arguments. Expected JSON with \"url\".");
        }

        if (!AllowedMethods.Contains(method))
            return Fail($"Method '{method}' is not allowed.");

        if (!HostAllowlist.TryValidatePublicHttpUrl(url, httpCfg.AllowedHosts, out var uri, out var error))
            return new ToolExecutionResult { Output = error, ExitCode = 403 };

        var timeout = TimeSpan.FromSeconds(httpCfg.TimeoutSeconds > 0 ? httpCfg.TimeoutSeconds : 30);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (IsBlockedHeader(key))
                    continue;
                if (!req.Headers.TryAddWithoutValidation(key, value))
                    req.Content ??= new StringContent(string.Empty);
            }
        }

        if (!string.IsNullOrEmpty(body)
            && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            // Re-validate final URI after redirects.
            if (response.RequestMessage?.RequestUri is { } finalUri
                && !HostAllowlist.IsHostAllowed(httpCfg.AllowedHosts, finalUri.Host))
            {
                return new ToolExecutionResult
                {
                    Output = $"Redirect target host '{finalUri.Host}' is not allowlisted.",
                    ExitCode = 403
                };
            }

            var media = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var truncated = text.Length > maxChars;
            if (truncated)
                text = text[..maxChars] + $"\n…[truncated {text.Length - maxChars} chars]";

            var sb = new StringBuilder();
            sb.AppendLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            sb.AppendLine($"content-type: {media}");
            sb.AppendLine($"url: {response.RequestMessage?.RequestUri ?? uri}");
            sb.AppendLine();
            sb.Append(text);

            return new ToolExecutionResult
            {
                Output = sb.ToString(),
                ExitCode = response.IsSuccessStatusCode ? 0 : (int)response.StatusCode,
                OutputTruncated = truncated
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail($"HTTP request timed out after {timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP tool failed for {Url}", uri);
            return Fail($"HTTP request failed: {ex.Message}");
        }
    }

    private static bool IsBlockedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

    private static string Args(OllamaToolCall toolCall) =>
        string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments;

    private static ToolExecutionResult Fail(string message) =>
        new() { Output = message, ExitCode = 1 };
}
