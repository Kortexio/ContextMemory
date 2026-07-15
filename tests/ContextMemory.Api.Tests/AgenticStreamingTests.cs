using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgentPartialResponseFormatterTests
{
    [Fact]
    public void FormatTimeoutResponse_WithLastAnswer_AppendsPartialNotice()
    {
        var result = AgentPartialResponseFormatter.FormatTimeoutResponse("Resposta quase pronta.", []);

        Assert.Contains("Resposta quase pronta", result);
        Assert.Contains("Partial answer", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatTimeoutResponse_WithSteps_SummarizesProgress()
    {
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = "{}",
                Output = "done",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(10)
            }
        };

        var result = AgentPartialResponseFormatter.FormatTimeoutResponse(null, steps);

        Assert.Contains("shell_execute", result);
        Assert.Contains("time limit", result, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AgenticStreamBufferTests
{
    [Fact]
    public void Stream_EmitsOnlyAssistantContent_NoToolCalls()
    {
        var chunks = AgenticStreamBuffer.Stream("test-model", "hello world", chunkSize: 5).ToList();

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks.Where(c => !c.Done), c =>
        {
            Assert.Null(c.Message?.ToolCalls);
            Assert.Equal("assistant", c.Message?.Role);
        });

        var full = string.Concat(chunks.Where(c => c.Message is not null).Select(c => c.Message!.Content));
        Assert.Equal("hello world", full);
        Assert.True(chunks[^1].Done);
    }
}

public sealed class AgenticTimeoutIntegrationTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticTimeoutIntegrationTests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.AgenticHandler.InfiniteToolLoop = true;
    }

    [Fact]
    public async Task Chat_WhenLoopTimesOut_ReturnsPartialResponseWithHeader()
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
                        MaxIterations = 10_000,
                        LoopTimeoutSeconds = 2
                    }
                }
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", "timeout-user");
        request.Headers.Add("X-Session-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            messages = new[] { new { role = "user", content = "Loop infinito de tools" } }
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Demorou demasiado: {sw.Elapsed}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("time limit", body, StringComparison.OrdinalIgnoreCase);

        Assert.True(
            response.Headers.TryGetValues("X-Context-Memory-Agentic-Timed-Out", out _),
            "Header X-Context-Memory-Agentic-Timed-Out em falta");
    }
}

public sealed class AgenticStreamingIntegrationTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticStreamingIntegrationTests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChatStream_WithAgenticEnabled_EmitsProgressThenFinalAnswer()
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
        request.Headers.Add("X-User-Id", "stream-user");
        request.Headers.Add("X-Session-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            stream = true,
            messages = new[] { new { role = "user", content = "Executa echo agentic-ok no shell" } }
        });

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Context-Memory-Agentic-Progress"));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tool_calls", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\":\"tool\"", raw, StringComparison.Ordinal);
        Assert.Contains("context_memory", raw, StringComparison.Ordinal);
        Assert.Contains("agentic", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shell_execute", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agentic-ok", raw, StringComparison.OrdinalIgnoreCase);
    }
}
