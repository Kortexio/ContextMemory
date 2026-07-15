using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Localization;
using System.Text.Json.Serialization;
using ContextMemory.Infrastructure.Agentic;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

public sealed class McpJsonRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<McpJsonRpcClient> _logger;
    private readonly McpOAuthTokenProvider _oauthTokenProvider;
    private int _requestId;

    public McpJsonRpcClient(
        HttpClient httpClient,
        McpOAuthTokenProvider oauthTokenProvider,
        ILogger<McpJsonRpcClient> logger)
    {
        _httpClient = httpClient;
        _oauthTokenProvider = oauthTokenProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        IntegrationToolConfig server,
        CancellationToken cancellationToken = default)
    {
        if (server.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return GetMockTools(server);

        await EnsureInitializedAsync(server, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(server, "tools/list", new { }, cancellationToken)
            .ConfigureAwait(false);

        if (response.Error is not null)
            throw new InvalidOperationException($"MCP tools/list failed: {response.Error.Message}");

        if (response.Result is not { } result || !result.TryGetProperty("tools", out var toolsElement))
            return [];

        var tools = new List<McpToolDefinition>();
        foreach (var tool in toolsElement.EnumerateArray())
        {
            var name = tool.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            object? schema = null;
            if (tool.TryGetProperty("inputSchema", out var schemaEl))
                schema = JsonSerializer.Deserialize<object>(schemaEl.GetRawText());

            tools.Add(new McpToolDefinition
            {
                ServerName = server.Name,
                Name = name,
                Description = tool.TryGetProperty("description", out var descEl) ? descEl.GetString() : null,
                InputSchema = schema
            });
        }

        return tools;
    }

    public async Task<string> CallToolAsync(
        IntegrationToolConfig server,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        if (server.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return ExecuteMock(server, toolName, argumentsJson);

        await EnsureInitializedAsync(server, cancellationToken).ConfigureAwait(false);

        object? argsObject = new { };
        if (!string.IsNullOrWhiteSpace(argumentsJson))
            argsObject = JsonSerializer.Deserialize<object>(argumentsJson) ?? new { };

        var response = await SendRequestAsync(
                server,
                "tools/call",
                new Dictionary<string, object?> { ["name"] = toolName, ["arguments"] = argsObject },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Error is not null)
            throw new InvalidOperationException($"MCP tools/call failed: {response.Error.Message}");

        return FormatToolResult(response.Result);
    }

    private async Task EnsureInitializedAsync(
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        var initResponse = await SendRequestAsync(
                server,
                "initialize",
                new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "contextmemory-agentic", version = "0.2.0" }
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (initResponse.Error is not null)
            throw new InvalidOperationException($"MCP initialize failed: {initResponse.Error.Message}");

        await SendNotificationAsync(server, "notifications/initialized", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<McpJsonRpcResponse> SendRequestAsync(
        IntegrationToolConfig server,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var request = new McpJsonRpcRequest
        {
            Id = Interlocked.Increment(ref _requestId),
            Method = method,
            Params = parameters
        };

        using var httpRequest = await BuildHttpRequestAsync(server, request, cancellationToken).ConfigureAwait(false);
        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "MCP {Method} HTTP {Status} for server {Server}: {Body}",
                method,
                (int)httpResponse.StatusCode,
                server.Name,
                body);
            throw new HttpRequestException($"MCP HTTP {(int)httpResponse.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<McpJsonRpcResponse>(body, JsonOptions)
                     ?? throw new InvalidOperationException("Empty MCP JSON-RPC response.");

        return parsed;
    }

    private async Task SendNotificationAsync(
        IntegrationToolConfig server,
        string method,
        CancellationToken cancellationToken)
    {
        var notification = new McpJsonRpcNotification { Method = method };
        using var httpRequest = await BuildHttpRequestAsync(server, notification, cancellationToken)
            .ConfigureAwait(false);
        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("MCP notification {Method} returned HTTP {Status}: {Body}", method, (int)httpResponse.StatusCode, body);
        }
    }

    private async Task<HttpRequestMessage> BuildHttpRequestAsync(
        IntegrationToolConfig server,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        await ApplyAuthAsync(server, request, cancellationToken).ConfigureAwait(false);
        return request;
    }

    private async Task ApplyAuthAsync(
        IntegrationToolConfig server,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (server.Headers is not null)
        {
            foreach (var (key, value) in server.Headers)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        var bearer = await _oauthTokenProvider
            .ResolveBearerTokenAsync(server, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(bearer))
            return;

        switch (server.AuthMode.ToLowerInvariant())
        {
            case "bearer":
            case "oauth-per-tenant":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                break;
            case "api-key":
                request.Headers.TryAddWithoutValidation("X-Api-Key", bearer);
                break;
        }
    }

    private static string FormatToolResult(JsonElement? result)
    {
        if (result is null)
            return string.Empty;

        if (result.Value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text))
                    parts.Add(text.GetString() ?? string.Empty);
                else
                    parts.Add(item.GetRawText());
            }

            return string.Join("\n", parts);
        }

        if (result.Value.TryGetProperty("isError", out var isError) && isError.GetBoolean())
            return result.Value.GetRawText();

        return result.Value.GetRawText();
    }

    private static IReadOnlyList<McpToolDefinition> GetMockTools(IntegrationToolConfig server) =>
    [
        new()
        {
            ServerName = server.Name,
            Name = "get_account",
            Description = "Obtém detalhes de uma conta de subscrição (mock MCP).",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    accountId = new { type = "string", description = "ID da conta" }
                },
                required = new[] { "accountId" }
            }
        }
    ];

    private static string ExecuteMock(IntegrationToolConfig server, string toolName, string argumentsJson)
    {
        if (toolName.Contains("fail", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(ToolExecutionMessages.McpMockToolFailed(server.Name, toolName));

        return $"[mock:{server.Name}] {toolName}({argumentsJson}) → ok";
    }
}
