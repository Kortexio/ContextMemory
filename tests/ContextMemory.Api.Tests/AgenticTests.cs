using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Infrastructure.Agentic.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticToolRegistryTests
{
    [Fact]
    public void BuildTools_ReturnsShellTool_WhenAcaShellConfigured()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = new AgenticToolsConfig
                {
                    Execution =
                    [
                        new ExecutionToolConfig
                        {
                            Type = "aca-session",
                            Runtime = "shell",
                            PoolEndpoint = "mock://local"
                        }
                    ]
                }
            }
        };

        var tools = AgenticToolRegistry.BuildTools(config);

        Assert.Single(tools);
        Assert.Equal(AgenticToolRegistry.ShellExecuteToolName, tools[0].Function.Name);
    }

    [Fact]
    public void BuildTools_ReturnsEmpty_WhenNoExecutionTools()
    {
        var config = new AppRuntimeConfig { AppId = "test" };
        Assert.Empty(AgenticToolRegistry.BuildTools(config));
    }
}

public sealed class DeterministicAgentValidatorTests
{
    private readonly DeterministicAgentValidator _validator = new();

    private static AgentValidationRequest Request(
        string answer,
        AppRuntimeConfig? config = null,
        IReadOnlyList<AgentExecutionStep>? steps = null) =>
        new()
        {
            FinalAnswer = answer,
            Steps = steps ?? [],
            RuntimeConfig = config ?? new AppRuntimeConfig { AppId = "test" }
        };

    [Fact]
    public async Task ValidateAsync_RejectsEmptyAnswer()
    {
        var result = await _validator.ValidateAsync(Request(""));

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.FeedbackForModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsValidAnswer()
    {
        var result = await _validator.ValidateAsync(Request("Resposta completa ao utilizador."));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsWhenToolFailedWithoutMention()
    {
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = """{"command":"ls"}""",
                Output = "error",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.FromMilliseconds(10)
            }
        };

        var result = await _validator.ValidateAsync(Request("Tudo correu bem.", steps: steps));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RequiresConfirmationForDestructiveActions()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Guardrails = new AgenticGuardrailsConfig
                {
                    RequireConfirmationFor = ["delete"],
                    RequireZeroExitCode = false
                }
            }
        };

        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = """{"command":"delete /tmp/test"}""",
                Output = "blocked",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.FromMilliseconds(10)
            }
        };

        var result = await _validator.ValidateAsync(Request("O delete falhou com erro técnico.", config, steps));

        Assert.False(result.IsValid);
        Assert.Contains("confirmation", result.FeedbackForModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_AllowsSuccessfulDestructiveStepAfterHitl()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Guardrails = new AgenticGuardrailsConfig
                {
                    RequireConfirmationFor = ["delete"]
                }
            }
        };

        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = """{"command":"delete /tmp/test"}""",
                Output = "deleted",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(10)
            }
        };

        var result = await _validator.ValidateAsync(Request("Ficheiros apagados com sucesso.", config, steps));
        Assert.True(result.IsValid);
    }
}

public sealed class AgenticIntegrationTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticIntegrationTests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Chat_WithAgenticEnabled_ExecutesToolLoopAndReturnsFinalAnswer()
    {
        using var scope = _factory.Services.CreateScope();
        var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();

        await configStore.UpdateAsync(
            "demo-app",
            new AppConfigPatchRequest
            {
                Agentic = new AgenticConfig
                {
                    Enabled = true,
                    Tools = new AgenticToolsConfig
                    {
                        Execution =
                        [
                            new ExecutionToolConfig
                            {
                                Type = "aca-session",
                                Runtime = "shell",
                                PoolEndpoint = "mock://local"
                            }
                        ]
                    },
                    Guardrails = new AgenticGuardrailsConfig
                    {
                        MaxIterations = 5,
                        ValidationMode = "deterministic"
                    }
                }
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", "agentic-user");
        request.Headers.Add("X-Session-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            messages = new[] { new { role = "user", content = "Executa echo agentic-ok no shell" } }
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("agentic-ok", body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, _factory.AgenticHandler.ChatRequests.Count);
        // demo-app resolves to Qwen profile on backend "ollama" → client-side tool parsing
        // (ClientSideToolCalling) is used instead of native tools[] to avoid Ollama's Qwen
        // chat-template XML tool-parser 500s (see LlmCapabilities.PreferClientSideToolParsing).
        Assert.Contains("## Tool catalog", _factory.AgenticHandler.ChatRequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\"tools\"", _factory.AgenticHandler.ChatRequestBodies[0], StringComparison.Ordinal);
    }
}

public sealed class AcaExecutionToolExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShellMock_ReturnsSuccess()
    {
        var client = new AcaDynamicSessionsClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaDynamicSessionsClient>.Instance);
        var executor = new AcaExecutionToolExecutor(
            client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaExecutionToolExecutor>.Instance);

        var config = BuildConfig("shell");
        var toolCall = new OllamaToolCall(
            new OllamaFunctionCall("shell_execute", """{"command":"echo hello"}"""));

        var result = await executor.ExecuteAsync(toolCall, "test", config);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_PythonMock_ReturnsSuccess()
    {
        var client = new AcaDynamicSessionsClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaDynamicSessionsClient>.Instance);
        var executor = new AcaExecutionToolExecutor(
            client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaExecutionToolExecutor>.Instance);

        var config = BuildConfig("python");
        var toolCall = new OllamaToolCall(
            new OllamaFunctionCall("python_execute", """{"code":"print('py-ok')"}"""));

        var result = await executor.ExecuteAsync(toolCall, "test", config);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("py-ok", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NodeMock_ReturnsSuccess()
    {
        var client = new AcaDynamicSessionsClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaDynamicSessionsClient>.Instance);
        var executor = new AcaExecutionToolExecutor(
            client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaExecutionToolExecutor>.Instance);

        var config = BuildConfig("node");
        var toolCall = new OllamaToolCall(
            new OllamaFunctionCall("node_execute", """{"code":"console.log('node-ok')"}"""));

        var result = await executor.ExecuteAsync(toolCall, "test", config);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("node-ok", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_RestrictedEgress_BlocksUnknownEndpoint()
    {
        var client = new AcaDynamicSessionsClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaDynamicSessionsClient>.Instance);
        var executor = new AcaExecutionToolExecutor(
            client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AcaExecutionToolExecutor>.Instance);

        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    Execution =
                    [
                        new ExecutionToolConfig
                        {
                            Type = "aca-session",
                            Runtime = "shell",
                            PoolEndpoint = "https://evil.example.com/pool"
                        }
                    ]
                },
                Guardrails = new AgenticGuardrailsConfig { NetworkEgress = "restricted" }
            }
        };

        var toolCall = new OllamaToolCall(
            new OllamaFunctionCall("shell_execute", """{"command":"echo blocked"}"""));

        var result = await executor.ExecuteAsync(toolCall, "test", config);

        Assert.Equal(403, result.ExitCode);
        Assert.Contains("Egress", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static AppRuntimeConfig BuildConfig(string runtime) =>
        new()
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    Execution =
                    [
                        new ExecutionToolConfig
                        {
                            Type = "aca-session",
                            Runtime = runtime,
                            PoolEndpoint = "mock://local"
                        }
                    ]
                }
            }
        };
}

public sealed class McpToolNamingTests
{
    [Fact]
    public void ToQualifiedName_FormatsServerAndTool()
    {
        var name = ContextMemory.Core.Agentic.Mcp.McpToolNaming.ToQualifiedName("zuora-mcp", "get_account");
        Assert.Equal("zuora-mcp__get_account", name);
    }

    [Fact]
    public void TryParseQualifiedName_RoundTrips()
    {
        var ok = ContextMemory.Core.Agentic.Mcp.McpToolNaming.TryParseQualifiedName(
            "zuora-mcp__get_account", out var server, out var tool);

        Assert.True(ok);
        Assert.Equal("zuora-mcp", server);
        Assert.Equal("get_account", tool);
    }

    [Theory]
    [InlineData("zuora-developer-mcp-PACCAR-ACCP", "zuora-developer-mcp-PACCAR-ACCP", true)]
    // Gemma (native Ollama function-calling grammar) rewrites '-' as '_' in generated
    // identifiers; dispatch must still match the configured integration.
    [InlineData("zuora-developer-mcp-PACCAR-ACCP", "zuora_developer_mcp-PACCAR-ACCP", true)]
    [InlineData("zuora-developer-mcp-PACCAR-ACCP", "ZUORA-DEVELOPER-MCP-PACCAR-ACCP", true)]
    [InlineData("zuora-developer-mcp-PACCAR-ACCP", "other-mcp-server", false)]
    public void ServerNamesMatch_TreatsHyphenAndUnderscoreAsEquivalent(
        string configured, string parsed, bool expected)
    {
        Assert.Equal(
            expected,
            ContextMemory.Core.Agentic.Mcp.McpToolNaming.ServerNamesMatch(configured, parsed));
    }
}

public sealed class McpJsonRpcClientTests
{
    [Fact]
    public async Task ListToolsAsync_MockUrl_ReturnsTools()
    {
        var credentials = new StubMcpCredentialStore();
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var stdio = new ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient(
            credentials,
            new SingleHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new ContextMemory.Core.Configuration.ContextMemoryOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(),
            credentials,
            oauth,
            stdio,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig
        {
            Name = "zuora-mcp",
            Url = "mock://local",
            Type = "mcp"
        };

        var tools = await client.ListToolsAsync("demo-app", server);

        Assert.Single(tools);
        Assert.Equal("get_account", tools[0].Name);
        Assert.Equal("zuora-mcp__get_account", tools[0].QualifiedName);
    }

    [Fact]
    public async Task CallToolAsync_MockUrl_ReturnsOutput()
    {
        var credentials = new StubMcpCredentialStore();
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var stdio = new ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient(
            credentials,
            new SingleHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new ContextMemory.Core.Configuration.ContextMemoryOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(),
            credentials,
            oauth,
            stdio,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig { Name = "zuora-mcp", Url = "mock://local" };
        var output = await client.CallToolAsync("demo-app", server, "get_account", """{"accountId":"A-001"}""");

        Assert.Contains("A-001", output.Raw);
        Assert.Contains("zuora-mcp", output.Summary);
    }

    [Theory]
    [InlineData(
        """
        event: message
        data: {"jsonrpc":"2.0","id":1,"result":{"ok":true}}

        """,
        """{"jsonrpc":"2.0","id":1,"result":{"ok":true}}""")]
    [InlineData(
        """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""",
        """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""")]
    public void ExtractJsonRpcPayload_SupportsSseAndPlainJson(string body, string expectedJson)
    {
        var payload = ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient.ExtractJsonRpcPayload(body);
        Assert.Equal(expectedJson.Trim(), payload.Trim());
    }

    [Fact]
    public async Task ListToolsAsync_HttpSse_ParsesToolsAndSendsSession()
    {
        var handler = new SseMcpHandler();
        var credentials = new StubMcpCredentialStore();
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var stdio = new ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient(
            credentials,
            new SingleHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new ContextMemory.Core.Configuration.ContextMemoryOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(handler),
            credentials,
            oauth,
            stdio,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig
        {
            Name = "github",
            Url = "https://mcp.example.test/",
            Transport = "http",
            AuthMode = "bearer",
            AuthToken = "test-token",
            Type = "mcp",
            TimeoutSeconds = 30
        };

        var tools = await client.ListToolsAsync("demo-app", server);

        Assert.Equal(3, handler.RequestCount);
        Assert.Contains(handler.SessionIdsSeen, id => id == "session-abc");
        Assert.Single(tools);
        Assert.Equal("list_repos", tools[0].Name);
        Assert.Equal("github__list_repos", tools[0].QualifiedName);
    }

    [Fact]
    public async Task ListToolsAsync_MockStdio_ReturnsTools()
    {
        var credentials = new StubMcpCredentialStore();
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var stdio = new ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient(
            credentials,
            new SingleHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new ContextMemory.Core.Configuration.ContextMemoryOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpStdioClient>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(),
            credentials,
            oauth,
            stdio,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig
        {
            Name = "generic-mcp",
            Transport = "stdio",
            Command = "mock-stdio",
            Type = "mcp"
        };

        var tools = await client.ListToolsAsync("demo-app", server);
        Assert.Single(tools);
        Assert.Equal("generic-mcp__get_account", tools[0].QualifiedName);
    }

    [Fact]
    public void IntegrationToolConfig_InfersStdioFromCommand()
    {
        var server = new IntegrationToolConfig
        {
            Name = "jira",
            Command = "npx",
            Args = ["-y", "@mcp/jira"]
        };

        Assert.True(server.IsStdioTransport);
        Assert.True(server.IsConfigured);
    }

    [Fact]
    public void McpStdioPathNormalizer_RewritesWindowsZuoraPaths()
    {
        var (command, args, _) = ContextMemory.Infrastructure.Agentic.Mcp.McpStdioPathNormalizer.NormalizeForLinuxContainer(
            @"C:\Program Files\nodejs\node.exe",
            [@"C:\Users\vitor\.cursor\zuora-mcp-runtime\node_modules\zuora-mcp\dist\index.cjs"],
            null);

        Assert.Equal("node", command);
        Assert.Equal("/opt/mcps/zuora-mcp/dist/index.cjs", args[0]);
    }

    [Fact]
    public void McpToolSelector_PrioritizesRelevantAndRecentTools()
    {
        var selector = new McpToolSelector();
        var config = new AppRuntimeConfig
        {
            AppId = "demo",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    MaxMcpToolsPerTurn = 2
                }
            }
        };
        var tools = new[]
        {
            new McpToolDefinition { ServerName = "jira", Name = "search_issues", Description = "Search Jira issues" },
            new McpToolDefinition { ServerName = "jira", Name = "get_issue", Description = "Get Jira issue" },
            new McpToolDefinition { ServerName = "confluence", Name = "search_pages", Description = "Search pages" }
        };

        var selected = selector.SelectTools(config, tools, "find jira issue PAC-668", ["confluence__search_pages"]);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, t => t.QualifiedName == "jira__search_issues");
        Assert.Contains(selected, t => t.QualifiedName == "jira__get_issue");
    }

    [Fact]
    public void McpToolAccess_FilterCatalog_AppliesAllowAndDenyLists()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "companybrain",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    Integrations =
                    [
                        new IntegrationToolConfig
                        {
                            Type = "mcp",
                            Name = "zuora-developer-mcp-PACCAR-ACCP",
                            Enabled = true,
                            ToolAllowlist = ["query_objects", "ask_zuora", "get_account_summary"],
                            ToolDenylist = ["ping"]
                        }
                    ]
                }
            }
        };

        var tools = new[]
        {
            new McpToolDefinition { ServerName = "zuora-developer-mcp-PACCAR-ACCP", Name = "query_objects" },
            new McpToolDefinition { ServerName = "zuora-developer-mcp-PACCAR-ACCP", Name = "ping" },
            new McpToolDefinition { ServerName = "zuora-developer-mcp-PACCAR-ACCP", Name = "manage_bulk_actions" },
            new McpToolDefinition { ServerName = "zuora-developer-mcp-PACCAR-ACCP", Name = "ask_zuora" }
        };

        var filtered = McpToolAccess.FilterCatalog(config, tools);

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, t => t.Name == "query_objects");
        Assert.Contains(filtered, t => t.Name == "ask_zuora");
        Assert.DoesNotContain(filtered, t => t.Name == "ping");
        Assert.DoesNotContain(filtered, t => t.Name == "manage_bulk_actions");
    }

    private sealed class StubMcpCredentialStore : IMcpCredentialStore
    {
        public Task<McpCredentialRecord?> GetAsync(
            string appId,
            string integrationName,
            string? credentialRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<McpCredentialRecord?>(null);

        public Task<IReadOnlyList<McpCredentialRecord>> ListAsync(
            string appId,
            string? integrationName = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpCredentialRecord>>([]);

        public Task UpsertAsync(McpCredentialRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class SseMcpHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string?> SessionIdsSeen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            SessionIdsSeen.Add(
                request.Headers.TryGetValues("Mcp-Session-Id", out var values)
                    ? values.FirstOrDefault()
                    : null);

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            string sse;
            if (body.Contains("\"initialize\"", StringComparison.Ordinal))
            {
                sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"capabilities\":{},\"protocolVersion\":\"2024-11-05\"}}\n\n";
            }
            else if (body.Contains("notifications/initialized", StringComparison.Ordinal))
            {
                sse = "event: message\ndata: {}\n\n";
            }
            else
            {
                sse =
                    "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"list_repos\",\"description\":\"List repositories\",\"inputSchema\":{\"type\":\"object\"}}]}}\n\n";
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-abc");
            return response;
        }
    }
}

public sealed class McpAgenticIntegrationTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpAgenticIntegrationTests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Chat_WithMcpIntegration_ExecutesMcpToolAndReturnsAnswer()
    {
        using var scope = _factory.Services.CreateScope();
        var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();

        await configStore.UpdateAsync(
            "demo-app",
            new AppConfigPatchRequest
            {
                Agentic = new AgenticConfig
                {
                    Enabled = true,
                    Tools = new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora-mcp",
                                Url = "mock://zuora",
                                AuthMode = "bearer",
                                AuthToken = "test-token"
                            }
                        ]
                    },
                    Guardrails = new AgenticGuardrailsConfig
                    {
                        MaxIterations = 5,
                        ValidationMode = "deterministic"
                    }
                }
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", "mcp-user");
        request.Headers.Add("X-Session-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            messages = new[] { new { role = "user", content = "Consulta a conta A-001 no Zuora" } }
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("A-001", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active", body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, _factory.AgenticHandler.ChatRequests.Count);
    }
}
