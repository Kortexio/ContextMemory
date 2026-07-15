using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;
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
                    Guardrails = new AgenticGuardrailsConfig { MaxIterations = 5 }
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
        var firstBody = await _factory.AgenticHandler.ChatRequests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"tools\"", firstBody, StringComparison.Ordinal);
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
}

public sealed class McpJsonRpcClientTests
{
    [Fact]
    public async Task ListToolsAsync_MockUrl_ReturnsTools()
    {
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(),
            oauth,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig
        {
            Name = "zuora-mcp",
            Url = "mock://local",
            Type = "mcp"
        };

        var tools = await client.ListToolsAsync(server);

        Assert.Single(tools);
        Assert.Equal("get_account", tools[0].Name);
        Assert.Equal("zuora-mcp__get_account", tools[0].QualifiedName);
    }

    [Fact]
    public async Task CallToolAsync_MockUrl_ReturnsOutput()
    {
        var oauth = new ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpOAuthTokenProvider>.Instance);
        var client = new ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient(
            new HttpClient(),
            oauth,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextMemory.Infrastructure.Agentic.Mcp.McpJsonRpcClient>.Instance);

        var server = new IntegrationToolConfig { Name = "zuora-mcp", Url = "mock://local" };
        var output = await client.CallToolAsync(server, "get_account", """{"accountId":"A-001"}""");

        Assert.Contains("A-001", output);
        Assert.Contains("zuora-mcp", output);
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
                    Guardrails = new AgenticGuardrailsConfig { MaxIterations = 5 }
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
        var firstBody = await _factory.AgenticHandler.ChatRequests[0].Content!.ReadAsStringAsync();
        Assert.Contains("zuora-mcp__get_account", firstBody, StringComparison.Ordinal);
    }
}
