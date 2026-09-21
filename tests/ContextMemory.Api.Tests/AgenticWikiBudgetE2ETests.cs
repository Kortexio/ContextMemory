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
/// E2E: weak-model-style stubborn wiki_search after budget must force a final answer
/// (app- and LLM-server-agnostic circuit breaker).
/// </summary>
public sealed class AgenticWikiBudgetE2ETests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgenticWikiBudgetE2ETests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.AgenticHandler.InfiniteToolLoop = false;
        _factory.AgenticHandler.RejectFirstFinalAnswer = false;
        _factory.AgenticHandler.StubbornWikiBudgetLoop = false;
    }

    [Fact]
    public async Task StubbornWikiAfterBudget_ForcesAnswerWithoutMaxIterations()
    {
        _factory.AgenticHandler.StubbornWikiBudgetLoop = true;
        var sessionId = Guid.NewGuid().ToString("N");
        await EnableWikiAndMcpAsync();
        await SeedWikiDocAsync();

        using var request = CreateChatRequest(
            sessionId,
            "wiki-budget-user",
            "Quais são as regras de negócio PACCAR para validação ITD?");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("context_memory", out var cm));
        Assert.True(cm.TryGetProperty("agentic", out var agentic));

        // Must complete (or at least not sit forever on max-iterations HITL without an answer).
        Assert.False(
            agentic.TryGetProperty("awaitingConfirmation", out var awaiting) && awaiting.GetBoolean(),
            "não deve ficar em HITL max-iterations");

        Assert.True(agentic.TryGetProperty("steps", out var steps));
        var stepList = steps.EnumerateArray().ToList();
        Assert.True(stepList.Count >= 2, $"esperava wiki_search+wiki_grep; steps={stepList.Count}");

        var budgetRejections = stepList.Count(s =>
            s.TryGetProperty("summary", out var summary)
            && summary.GetString()?.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(budgetRejections >= 2, $"esperava >=2 rejeições de budget; got {budgetRejections}");

        // Circuit breaker must stop the burn — not run to the full maxIterations wall.
        Assert.True(
            stepList.Count < 20,
            $"circuit breaker falhou: {stepList.Count} steps (ainda a queimar iterações)");

        var answer = "";
        if (doc.RootElement.TryGetProperty("message", out var msg)
            && msg.TryGetProperty("content", out var contentEl))
        {
            answer = contentEl.GetString() ?? "";
        }

        Assert.True(
            answer.Contains("FORCE_ANSWER_OK", StringComparison.Ordinal)
            || body.Contains("FORCE_ANSWER_OK", StringComparison.Ordinal),
            $"esperava FORCE_ANSWER_OK no final; answer='{answer}'; steps={stepList.Count}; bodyPrefix={body[..Math.Min(400, body.Length)]}");
    }

    private async Task EnableWikiAndMcpAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();
        await configStore.UpdateAsync(
            "demo-app",
            new AppConfigPatchRequest
            {
                GlobalWikiEnabled = true,
                DefaultLanguage = "pt",
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
                        MaxIterations = 24,
                        ValidationMode = "deterministic",
                        HumanReviewOnMaxIterations = true
                    }
                }
            });
    }

    private async Task SeedWikiDocAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var wiki = scope.ServiceProvider.GetRequiredService<IGlobalWikiStore>();
        await wiki.UpsertAsync(
            "demo-app",
            "paccar-itd-validation",
            new GlobalWikiUpsertRequest
            {
                Title = "PACCAR ITD Validation Guide",
                Content =
                    "Business rules for PACCAR subscription creation and ITD message validation. "
                    + "Direct API testing is the recommended validation method.",
                SourceId = "e2e-wiki-budget"
            });
    }

    private HttpRequestMessage CreateChatRequest(string sessionId, string userId, string userMessage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Session-Id", sessionId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");
        request.Content = JsonContent.Create(new
        {
            model = "qwen3.5:9b",
            stream = false,
            messages = new[] { new { role = "user", content = userMessage } }
        });
        return request;
    }
}
