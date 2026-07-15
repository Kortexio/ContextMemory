using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgentJudgeResponseParserTests
{
    [Fact]
    public void Parse_AcceptsValidJson()
    {
        var result = AgentJudgeResponseParser.Parse("""{"valid":true,"feedback":""}""");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Parse_RejectsInvalidWithFeedback()
    {
        var result = AgentJudgeResponseParser.Parse(
            """{"valid":false,"feedback":"Falta detalhe sobre o erro."}""");

        Assert.False(result.IsValid);
        Assert.Contains("detalhe", result.FeedbackForModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ExtractsJsonFromMarkdownFence()
    {
        var result = AgentJudgeResponseParser.Parse(
            """
            Avaliação:
            {"valid": false, "feedback": "Incompleto"}
            """);

        Assert.False(result.IsValid);
    }
}

public sealed class HybridAgentValidatorTests
{
    private readonly DeterministicAgentValidator _deterministic = new();

    [Fact]
    public async Task Deterministic_RejectsBlockedPattern()
    {
        var request = new AgentValidationRequest
        {
            FinalAnswer = "Esta resposta contém segredo-interno.",
            RuntimeConfig = new AppRuntimeConfig
            {
                AppId = "test",
                Agentic = new AgenticConfig
                {
                    Guardrails = new AgenticGuardrailsConfig
                    {
                        BlockedAnswerPatterns = ["segredo-interno"]
                    }
                }
            }
        };

        var result = await _deterministic.ValidateAsync(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Deterministic_RejectsShortAnswer_WhenMinLengthConfigured()
    {
        var request = new AgentValidationRequest
        {
            FinalAnswer = "ok",
            RuntimeConfig = new AppRuntimeConfig
            {
                AppId = "test",
                Agentic = new AgenticConfig
                {
                    Guardrails = new AgenticGuardrailsConfig { MinAnswerLength = 20 }
                }
            }
        };

        var result = await _deterministic.ValidateAsync(request);
        Assert.False(result.IsValid);
    }
}

public sealed class HybridAgentValidationIntegrationTests : IClassFixture<AgenticStubWebApplicationFactory>
{
    private readonly AgenticStubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HybridAgentValidationIntegrationTests(AgenticStubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Chat_WithHybridValidationAndJudgeRejection_RetriesAndSucceeds()
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
                        ValidationMode = "hybrid"
                    }
                }
            });

        _factory.AgenticHandler.RejectFirstFinalAnswer = true;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Headers.Add("X-App-Id", "demo-app");
        request.Headers.Add("X-User-Id", "judge-user");
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
        Assert.True(_factory.AgenticHandler.ChatRequests.Count >= 3);
    }
}
