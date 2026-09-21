using System.Text.Json;
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
            UrlFetchConfigJson(),
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
            UrlFetchConfigJson(),
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
            UrlFetchConfigJson(),
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
            UrlFetchConfigJson(),
            Config(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public async Task DeterministicValidator_Rejects_Hallucinated_Site_Answer()
    {
        var validator = new DeterministicAgentValidator();
        var urlGuardrail = AgenticCatalogSeed.Guardrails.First(g => g.Id == "url-fetch-required");
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
                        ActiveGuardrails = [urlGuardrail with { ConfigJson = UrlFetchConfigJson() }]
                    }
                },
                UserObjective = "e esse site aqui? sobre o que é?\nhttps://www.kortexio.io/"
            });

        Assert.False(result.IsValid);
        Assert.Contains("without fetching", result.FeedbackForModel ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static string UrlFetchConfigJson() =>
        JsonSerializer.Serialize(new
        {
            kind = AgenticGuardrailKinds.UrlFetch,
            feedback =
                "Rejected: you described a website/URL without fetching it (hosts: {hosts}). "
                + "Emit tool_calls first — e.g. python_execute with httpx/Playwright or web-search — "
                + "then answer ONLY from tool output.",
            aboutSiteMarkers = new[]
            {
                "this site", "this website", "this page", "this url", "this link",
                "the website", "the site", "what is", "what's this", "whats this", "what about",
                "open ", "visit ", "fetch", "scrape", "summary", "content of"
            },
            fetchToolMarkers = new[]
            {
                "python_execute", "shell_execute", "node_execute", "web_search", "fetch_url", "http_request",
                "browser_navigate", "browser_snapshot", "browser_screenshot", "read_image", "brave", "tavily",
                "ddgs", "duckduckgo", "playwright", "httpx", "requests", "curl"
            }
        });
}
