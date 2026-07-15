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

/// <summary>
/// E2E: pedido HTTP → orchestrator → action (shell/MCP) → progresso em stream → resposta final → wiki log.
/// </summary>
public sealed class AgenticActionsE2ETests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticActionsE2ETests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.AgenticHandler.InfiniteToolLoop = false;
        _factory.AgenticHandler.RejectFirstFinalAnswer = false;
    }

    [Fact]
    public async Task ShellAction_Stream_EmitsProgressStepsAndFinalAnswer()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableShellActionAsync();

        using var request = CreateChatRequest(
            sessionId,
            "shell-user",
            stream: true,
            "Executa echo agentic-ok no shell");

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Context-Memory-Agentic-Progress"));

        var lines = await ReadNdjsonLinesAsync(response);
        var progressPhases = ExtractProgressPhases(lines);
        var finalAnswer = string.Concat(
            lines
                .Where(l => l.Message?.Content is { Length: > 0 })
                .Select(l => l.Message!.Content));

        Assert.Contains("Started", progressPhases);
        Assert.Contains("LlmRequest", progressPhases);
        Assert.Contains("ToolStarted", progressPhases);
        Assert.Contains("ToolCompleted", progressPhases);
        Assert.Contains("Validating", progressPhases);
        Assert.Contains("Completed", progressPhases);

        Assert.Contains("shell_execute", string.Join('\n', progressPhases.Concat(
            lines.Select(l => l.ContextMemory?.Agentic?.Label ?? ""))), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agentic-ok", finalAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"role\":\"tool\"", string.Join('\n', lines.Select(l => JsonSerializer.Serialize(l, JsonOptions))), StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\": \"tool\"", string.Join('\n', lines.Select(l => JsonSerializer.Serialize(l, JsonOptions))), StringComparison.Ordinal);

        await AssertWikiLogContainsAsync(sessionId, "shell-user", "shell_execute", "agentic");
    }

    [Fact]
    public async Task McpAction_Stream_EmitsProgressStepsAndFinalAnswer()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableMcpActionAsync();

        using var request = CreateChatRequest(
            sessionId,
            "mcp-user",
            stream: true,
            "Consulta a conta A-001 no Zuora");

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lines = await ReadNdjsonLinesAsync(response);
        var progressPhases = ExtractProgressPhases(lines);
        var finalAnswer = string.Concat(
            lines
                .Where(l => l.Message?.Content is { Length: > 0 })
                .Select(l => l.Message!.Content));

        Assert.Contains("ToolStarted", progressPhases);
        Assert.Contains("ToolCompleted", progressPhases);
        Assert.Contains("A-001", finalAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active", finalAnswer, StringComparison.OrdinalIgnoreCase);

        var toolLabels = lines
            .Select(l => l.ContextMemory?.Agentic?.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!);
        Assert.Contains(toolLabels, l => l.Contains("zuora-mcp__get_account", StringComparison.OrdinalIgnoreCase)
            || l.Contains("get_account", StringComparison.OrdinalIgnoreCase));

        await AssertWikiLogContainsAsync(sessionId, "mcp-user", "zuora-mcp__get_account", "agentic");
    }

    [Fact]
    public async Task ShellAction_NonStream_ReturnsAgenticMetadataAndWikiLog()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableShellActionAsync();

        using var request = CreateChatRequest(
            sessionId,
            "shell-sync-user",
            stream: false,
            "Executa echo agentic-ok no shell");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("context_memory", out var cm));
        Assert.True(cm.TryGetProperty("agentic", out var agentic));
        Assert.True(agentic.TryGetProperty("steps", out var steps));
        Assert.True(steps.GetArrayLength() >= 1);
        Assert.Contains("agentic-ok", body, StringComparison.OrdinalIgnoreCase);

        await AssertWikiLogContainsAsync(sessionId, "shell-sync-user", "shell_execute", "sucesso");
    }

    private async Task EnableShellActionAsync()
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
    }

    private async Task EnableMcpActionAsync()
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
    }

    private HttpRequestMessage CreateChatRequest(
        string sessionId,
        string userId,
        bool stream,
        string userMessage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Session-Id", sessionId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            stream,
            messages = new[] { new { role = "user", content = userMessage } }
        });
        return request;
    }

    private static async Task<List<OllamaResponse>> ReadNdjsonLinesAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var lines = new List<OllamaResponse>();

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var chunk = JsonSerializer.Deserialize<OllamaResponse>(line.Trim(), JsonOptions);
            if (chunk is not null)
                lines.Add(chunk);
        }

        return lines;
    }

    private static List<string> ExtractProgressPhases(IEnumerable<OllamaResponse> lines) =>
        lines
            .Select(l => l.ContextMemory?.Agentic?.Phase)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct()
            .ToList();

    private async Task AssertWikiLogContainsAsync(
        string sessionId,
        string userId,
        params string[] expectedFragments)
    {
        using var scope = _factory.Services.CreateScope();
        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var snapshot = await sessionStore.LoadAsync("demo-app", userId, sessionId);

        Assert.False(string.IsNullOrWhiteSpace(snapshot.LogMd), "log.md não foi criado na sessão");

        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, snapshot.LogMd, StringComparison.OrdinalIgnoreCase);
        }
    }
}
