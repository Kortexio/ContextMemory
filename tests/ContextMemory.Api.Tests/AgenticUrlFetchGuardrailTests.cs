using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticUrlFetchGuardrailTests
{
    private static AppRuntimeConfig Config() => new()
    {
        AppId = "test",
        DefaultLanguage = "pt-PT",
        Agentic = new AgenticConfig { Enabled = true }
    };

    [Fact]
    public void Rejects_SiteDescription_Without_Fetch()
    {
        var objective = "e esse site aqui? sobre o que é?\nhttps://www.kortexio.io/";
        var answer =
            "https://www.kortexio.io/ é o portal do Kortex, uma plataforma de IA generativa para business.";

        var hit = AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
            objective,
            answer,
            [],
            Config(),
            out var feedback);

        Assert.True(hit);
        Assert.Contains("kortexio.io", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_calls", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_When_PythonFetch_Succeeded()
    {
        var objective = "sobre o que é https://www.kortexio.io/ ?";
        var answer = "É a camada de memória + agentic para o teu LLM.";

        var hit = AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
            objective,
            answer,
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "python_execute",
                    Success = true,
                    ExitCode = 0,
                    Arguments = """{"code":"import httpx; print(httpx.get('https://www.kortexio.io/').text[:500])"}""",
                    Output = "title: Kortexio — Memory + agentic layer for your LLM",
                    Duration = TimeSpan.FromMilliseconds(200)
                }
            ],
            Config(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Allows_Reporting_Failed_Fetch()
    {
        var objective = "abre https://www.kortexio.io/ e diz o título";
        var answer = "Não consegui abrir o site: timeout.";

        var hit = AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
            objective,
            answer,
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "python_execute",
                    Success = false,
                    ExitCode = 1,
                    Arguments = """{"code":"import httpx; httpx.get('https://kortexio.io/', timeout=5)"}""",
                    Output = "ConnectTimeout",
                    Duration = TimeSpan.FromSeconds(5)
                }
            ],
            Config(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Skips_When_No_Url_In_Objective()
    {
        var hit = AgenticUrlFetchGuardrail.TryGetRejectionFeedback(
            "quantas contas há no Zuora?",
            "Há 3 contas.",
            [],
            Config(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public async Task DeterministicValidator_Rejects_Hallucinated_Site_Answer()
    {
        var validator = new DeterministicAgentValidator();
        var result = await validator.ValidateAsync(
            new AgentValidationRequest
            {
                FinalAnswer =
                    "O Kortex é uma plataforma de IA generativa para business, diferente do Zuora.",
                Steps = [],
                RuntimeConfig = Config() with
                {
                    ResolvedPolicy = new ResolvedAgenticPolicy
                    {
                        ActiveGuardrailKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            AgenticGuardrailKinds.UrlFetch
                        },
                        ActiveGuardrails =
                        [
                            new AgenticGuardrailDefinition
                            {
                                Id = "url-fetch-required",
                                Name = "URL fetch required",
                                Kind = AgenticGuardrailKinds.UrlFetch,
                                ConfigJson = "{}"
                            }
                        ]
                    }
                },
                UserObjective = "e esse site aqui? sobre o que é?\nhttps://www.kortexio.io/"
            });

        Assert.False(result.IsValid);
        Assert.Contains("kortexio.io", result.FeedbackForModel ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
