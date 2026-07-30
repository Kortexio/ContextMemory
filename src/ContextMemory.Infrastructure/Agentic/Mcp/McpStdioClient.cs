using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Infrastructure.Agentic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

/// <summary>
/// MCP client over stdio (Cursor-style command/args/env processes).
/// When <see cref="ContextMemoryOptions.McpRuntimeUrl"/> is configured, execution is delegated
/// to the mcp-runtime sidecar instead of spawning processes inside the API container.
/// </summary>
public sealed class McpStdioClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IMcpCredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<McpStdioClient> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<StdioSession>>> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private int _requestId;

    public McpStdioClient(
        IMcpCredentialStore credentialStore,
        IHttpClientFactory httpClientFactory,
        IOptions<ContextMemoryOptions> options,
        ILogger<McpStdioClient> logger)
    {
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    private bool UseRemoteRuntime =>
        !string.IsNullOrWhiteSpace(_options.McpRuntimeUrl);

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken = default)
    {
        if (IsMock(server))
            return GetMockTools(server);

        if (UseRemoteRuntime)
            return await ListToolsRemoteAsync(appId, server, cancellationToken).ConfigureAwait(false);

        var session = await GetOrCreateSessionAsync(appId, server, cancellationToken).ConfigureAwait(false);
        var response = await session.SendRequestAsync("tools/list", new { }, cancellationToken).ConfigureAwait(false);
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

    public async Task<McpNormalizedResult> CallToolAsync(
        string appId,
        IntegrationToolConfig server,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        if (IsMock(server))
        {
            var mock = ExecuteMock(server, toolName, argumentsJson);
            return new McpNormalizedResult { Summary = mock, Raw = mock };
        }

        if (UseRemoteRuntime)
            return await CallToolRemoteAsync(appId, server, toolName, argumentsJson, cancellationToken)
                .ConfigureAwait(false);

        var session = await GetOrCreateSessionAsync(appId, server, cancellationToken).ConfigureAwait(false);

        object? argsObject = new { };
        if (!string.IsNullOrWhiteSpace(argumentsJson))
            argsObject = JsonSerializer.Deserialize<object>(argumentsJson) ?? new { };

        var response = await session
            .SendRequestAsync(
                "tools/call",
                new Dictionary<string, object?> { ["name"] = toolName, ["arguments"] = argsObject },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Error is not null)
            throw new InvalidOperationException($"MCP tools/call failed: {response.Error.Message}");

        return Normalize(FormatToolResult(response.Result));
    }

    private async Task<IReadOnlyList<McpToolDefinition>> ListToolsRemoteAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        var payload = await BuildRemotePayloadAsync(appId, server, cancellationToken).ConfigureAwait(false);
        using var response = await PostRemoteAsync("/v1/stdio/tools/list", payload, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"MCP runtime tools/list failed: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("tools", out var toolsElement))
        {
            return [];
        }

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

    private async Task<McpNormalizedResult> CallToolRemoteAsync(
        string appId,
        IntegrationToolConfig server,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var payload = await BuildRemotePayloadAsync(appId, server, cancellationToken).ConfigureAwait(false);
        payload["toolName"] = toolName;
        payload["arguments"] = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;

        using var response = await PostRemoteAsync("/v1/stdio/tools/call", payload, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(FormatRemoteCallFailure((int)response.StatusCode, body));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            return new McpNormalizedResult();

        return Normalize(FormatToolResult(result));
    }

    private static string FormatRemoteCallFailure(int statusCode, string body)
    {
        var hint = string.Empty;
        if (body.Contains("504", StringComparison.Ordinal)
            || body.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || body.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            hint =
                " Zuora MCP timed out (~60s on their gateway). Narrow the query, reduce result size, or call with help:true first.";
        }
        else if (body.Contains("sending the request", StringComparison.OrdinalIgnoreCase))
        {
            hint = " Transient network error to Zuora MCP — retry once.";
        }

        return $"MCP runtime tools/call failed: {statusCode} {body}.{hint}";
    }

    private async Task<Dictionary<string, object?>> BuildRemotePayloadAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        var env = await ResolveEnvAsync(appId, server, cancellationToken).ConfigureAwait(false);
        var (command, args, cwd) = McpStdioPathNormalizer.NormalizeForLinuxContainer(
            server.Command ?? string.Empty,
            server.Args,
            server.WorkingDirectory);

        return new Dictionary<string, object?>
        {
            ["command"] = command,
            ["args"] = args,
            ["env"] = env,
            ["cwd"] = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
            ["timeoutSeconds"] = server.TimeoutSeconds
        };
    }

    private async Task<HttpResponseMessage> PostRemoteAsync(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("McpRuntime");
        var baseUrl = _options.McpRuntimeUrl.TrimEnd('/');
        return await client
            .PostAsJsonAsync($"{baseUrl}{path}", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _sessions.Values)
        {
            try
            {
                if (entry.IsValueCreated)
                {
                    var session = await entry.Value.ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // best-effort shutdown
            }
        }

        _sessions.Clear();
    }

    private async Task<StdioSession> GetOrCreateSessionAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        var key = $"{appId}::{server.Name}";
        var lazy = _sessions.GetOrAdd(
            key,
            _ => new Lazy<Task<StdioSession>>(
                () => StartSessionAsync(appId, server, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var session = await lazy.Value.ConfigureAwait(false);
            if (!session.IsAlive)
            {
                _sessions.TryRemove(key, out _);
                await session.DisposeAsync().ConfigureAwait(false);
                return await GetOrCreateSessionAsync(appId, server, cancellationToken).ConfigureAwait(false);
            }

            return session;
        }
        catch
        {
            _sessions.TryRemove(key, out _);
            throw;
        }
    }

    private async Task<StdioSession> StartSessionAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
            throw new InvalidOperationException($"MCP stdio server '{server.Name}' has no command.");

        var env = await ResolveEnvAsync(appId, server, cancellationToken).ConfigureAwait(false);
        var psi = new ProcessStartInfo
        {
            FileName = server.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory)
                ? Environment.CurrentDirectory
                : server.WorkingDirectory
        };

        foreach (var arg in server.Args)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in env)
            psi.Environment[key] = value;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start MCP process for '{server.Name}'.");

        _logger.LogInformation(
            "Started MCP stdio process {ProcessId} for server {Server} app {AppId}",
            process.Id,
            server.Name,
            appId);

        var session = new StdioSession(process, server, () => Interlocked.Increment(ref _requestId), _logger);
        await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<Dictionary<string, string>> ResolveEnvAsync(
        string appId,
        IntegrationToolConfig server,
        CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (server.Env is not null)
        {
            foreach (var (key, value) in server.Env)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    env[key] = value;
            }
        }

        if (string.IsNullOrWhiteSpace(server.CredentialRef))
            return env;

        var secret = await _credentialStore
            .GetAsync(appId, server.Name, server.CredentialRef, cancellationToken)
            .ConfigureAwait(false);
        if (secret is null)
            return env;

        if (secret.Env is not null)
        {
            foreach (var (key, value) in secret.Env)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    env[key] = value;
            }
        }

        if (secret.OAuth is not null)
        {
            if (!string.IsNullOrWhiteSpace(secret.OAuth.ClientId))
                env.TryAdd("MCP_OAUTH_CLIENT_ID", secret.OAuth.ClientId);
            if (!string.IsNullOrWhiteSpace(secret.OAuth.ClientSecret))
                env.TryAdd("MCP_OAUTH_CLIENT_SECRET", secret.OAuth.ClientSecret);
            if (!string.IsNullOrWhiteSpace(secret.OAuth.TokenUrl))
                env.TryAdd("MCP_OAUTH_TOKEN_URL", secret.OAuth.TokenUrl);
            if (!string.IsNullOrWhiteSpace(secret.OAuth.Scope))
                env.TryAdd("MCP_OAUTH_SCOPE", secret.OAuth.Scope);
            if (!string.IsNullOrWhiteSpace(secret.OAuth.Audience))
                env.TryAdd("MCP_OAUTH_AUDIENCE", secret.OAuth.Audience);
        }

        if (!string.IsNullOrWhiteSpace(secret.BearerToken))
            env.TryAdd("MCP_BEARER_TOKEN", secret.BearerToken);
        if (!string.IsNullOrWhiteSpace(secret.ApiKey))
            env.TryAdd("MCP_API_KEY", secret.ApiKey);

        // zuora-mcp remote bridge default is 120s; keep it aligned with integration timeout.
        if (server.TimeoutSeconds > 0)
            env.TryAdd("REMOTE_MCP_TIMEOUT_MS", (server.TimeoutSeconds * 1000).ToString());

        return env;
    }

    private static bool IsMock(IntegrationToolConfig server) =>
        string.Equals(server.Command, "mock-stdio", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(server.Url)
            && server.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<McpToolDefinition> GetMockTools(IntegrationToolConfig server) =>
    [
        new()
        {
            ServerName = server.Name,
            Name = "get_account",
            Description = "Obtém detalhes de uma conta de subscrição (mock MCP stdio).",
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

        return $"[mock-stdio:{server.Name}] {toolName}({argumentsJson}) → ok";
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

        return result.Value.GetRawText();
    }

    private static McpNormalizedResult Normalize(string raw)
    {
        var trimmed = raw.Trim();
        var summary = trimmed;
        var entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var truncated = false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                        entities[prop.Name] = prop.Value.ToString();
                }
            }
        }
        catch
        {
            // keep text summary
        }

        if (summary.Length > 1200)
        {
            summary = summary[..1200].TrimEnd() + "…";
            truncated = true;
        }

        return new McpNormalizedResult
        {
            Summary = summary,
            Entities = entities,
            Raw = trimmed,
            Truncated = truncated
        };
    }

    private sealed class StdioSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly IntegrationToolConfig _server;
        private readonly Func<int> _nextId;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<McpJsonRpcResponse>> _pending = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _stdoutPump;
        private readonly Task _stderrPump;
        private bool _initialized;

        public StdioSession(
            Process process,
            IntegrationToolConfig server,
            Func<int> nextId,
            ILogger logger)
        {
            _process = process;
            _server = server;
            _nextId = nextId;
            _logger = logger;
            _stdoutPump = Task.Run(() => PumpStdoutAsync(_lifetime.Token));
            _stderrPump = Task.Run(() => PumpStderrAsync(_lifetime.Token));
        }

        public bool IsAlive => !_process.HasExited;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
                return;

            var init = await SendRequestAsync(
                    "initialize",
                    new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { },
                        clientInfo = new { name = "contextmemory-agentic", version = "0.3.0" }
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (init.Error is not null)
                throw new InvalidOperationException($"MCP initialize failed: {init.Error.Message}");

            await SendNotificationAsync("notifications/initialized", cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }

        public async Task<McpJsonRpcResponse> SendRequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var id = _nextId();
            var tcs = new TaskCompletionSource<McpJsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var payload = new McpJsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = parameters
            };

            await WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_server.TimeoutSeconds > 0)
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_server.TimeoutSeconds));
            else
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

            try
            {
                await using var reg = timeoutCts.Token.Register(() =>
                    tcs.TrySetException(new TimeoutException($"MCP stdio request '{method}' timed out.")));
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
        {
            var payload = new McpJsonRpcNotification { Method = method };
            await WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteLineAsync(string json, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task PumpStdoutAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    McpJsonRpcResponse? response;
                    try
                    {
                        response = JsonSerializer.Deserialize<McpJsonRpcResponse>(line, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogDebug(ex, "Ignoring non-JSON stdout from MCP {Server}: {Line}", _server.Name, line);
                        continue;
                    }

                    if (response is null)
                        continue;

                    if (response.Id.ValueKind is JsonValueKind.Number
                        && response.Id.TryGetInt32(out var id)
                        && _pending.TryGetValue(id, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP stdio stdout pump failed for {Server}", _server.Name);
            }
            finally
            {
                foreach (var pending in _pending.Values)
                    pending.TrySetException(new InvalidOperationException($"MCP stdio process '{_server.Name}' closed."));
            }
        }

        private async Task PumpStderrAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
                {
                    var line = await _process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (!string.IsNullOrWhiteSpace(line))
                        _logger.LogDebug("MCP stdio[{Server}] stderr: {Line}", _server.Name, line);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MCP stdio stderr pump ended for {Server}", _server.Name);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _lifetime.Cancel();
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }

            try { await Task.WhenAny(Task.WhenAll(_stdoutPump, _stderrPump), Task.Delay(1000)).ConfigureAwait(false); }
            catch { /* ignore */ }

            _writeLock.Dispose();
            _lifetime.Dispose();
            _process.Dispose();
        }
    }
}
