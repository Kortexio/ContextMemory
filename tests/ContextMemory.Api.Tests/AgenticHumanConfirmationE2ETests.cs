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

public sealed class AgenticHumanConfirmationE2ETests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticHumanConfirmationE2ETests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.AgenticHandler.InfiniteToolLoop = false;
    }

    [Fact]
    public async Task DestructiveAction_BlocksUntilUserConfirms_ThenExecutes()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableDestructiveGuardrailsAsync();

        // 1) Pedido inicial — deve pedir confirmação
        using (var request = CreateChatRequest(sessionId, "delete-user", "Executa delete no utilizador de teste"))
        {
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(
                response.Headers.Contains("X-Context-Memory-Agentic-Awaiting-Confirmation"),
                "Resposta deve indicar confirmação pendente no header.");

            var assistantContent = await ReadAssistantContentAsync(response);
            Assert.Contains("Human confirmation", assistantContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CONFIRM:", assistantContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("agentic-ok", assistantContent, StringComparison.OrdinalIgnoreCase);
        }

        using var scope = _factory.Services.CreateScope();
        var pendingStore = scope.ServiceProvider.GetRequiredService<IAgenticPendingStore>();
        var pending = await pendingStore.TryLoadAsync("demo-app", "delete-user", sessionId);
        Assert.NotNull(pending);

        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var snapshot = await sessionStore.LoadAsync("demo-app", "delete-user", sessionId);
        Assert.Contains("agentic-checkpoint", snapshot.LogMd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmation pending", snapshot.LogMd, StringComparison.OrdinalIgnoreCase);

        // 2) Confirmação — deve executar e concluir
        using (var confirmRequest = CreateChatRequest(sessionId, "delete-user", $"[CONFIRM:{pending!.PendingId}]"))
        {
            var response = await _client.SendAsync(confirmRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var assistantContent = await ReadAssistantContentAsync(response);
            Assert.Contains("agentic-ok", assistantContent, StringComparison.OrdinalIgnoreCase);
        }

        var cleared = await pendingStore.TryLoadAsync("demo-app", "delete-user", sessionId);
        Assert.Null(cleared);

        snapshot = await sessionStore.LoadAsync("demo-app", "delete-user", sessionId);
        Assert.Contains("confirmation received", snapshot.LogMd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DestructiveAction_UserCancels_DoesNotExecuteTool()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableDestructiveGuardrailsAsync();

        using (var request = CreateChatRequest(sessionId, "cancel-user", "Executa delete no ambiente"))
        {
            await _client.SendAsync(request);
        }

        using var cancelRequest = CreateChatRequest(sessionId, "cancel-user", "Cancel the operation");
        var cancelResponse = await _client.SendAsync(cancelRequest);
        var assistantContent = await ReadAssistantContentAsync(cancelResponse);

        Assert.Contains("cancelled", assistantContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agentic-ok", assistantContent, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnableDestructiveGuardrailsAsync()
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
                        ValidationMode = "deterministic",
                        RequireConfirmationFor = ["delete"]
                    }
                }
            });
    }

    private static async Task<string> ReadAssistantContentAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private static HttpRequestMessage CreateChatRequest(string sessionId, string userId, string userMessage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Session-Id", sessionId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "llama3.2",
            messages = new[] { new { role = "user", content = userMessage } }
        });
        return request;
    }
}
