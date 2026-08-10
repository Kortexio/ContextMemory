using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticLiveDataEvidenceGuardrailTests
{
    [Fact]
    public void Rejects_WhenLiveQuestionWithoutSuccessfulMcp()
    {
        var config = ConfigWithMcp();
        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "Find one canceled Zuora account",
            "Account A0001 is Canceled.",
            [],
            config,
            out var feedback);

        Assert.True(ok);
        Assert.Contains("tool_calls", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepts_WhenSuccessfulQueryObjects()
    {
        var config = ConfigWithMcp();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "zuora-dev__query_objects",
                Arguments = "{}",
                Output = """{"accountNumber":"A00006681","status":"Canceled"}""",
                Success = true
            }
        };

        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "Find one canceled Zuora account",
            "A00006681 Canceled",
            steps,
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Ignores_NonLiveQuestions()
    {
        var config = ConfigWithMcp();
        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "What is the capital of Portugal?",
            "Lisbon",
            [],
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Rejects_JiraTicketLookup_WithoutWikiEvidence()
    {
        var config = ConfigWithWiki();
        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "busque os tickets PAC-759, PAC-762 e PAC-769",
            "Vou buscar os tickets na wiki.",
            [],
            config,
            out var feedback);

        Assert.True(ok);
        Assert.Contains("wiki_search", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepts_JiraTicketLookup_WithSuccessfulWikiSearch()
    {
        var config = ConfigWithWiki();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"PAC-759"}""",
                Output = "PAC-759 body…",
                Success = true
            }
        };

        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "busque os tickets PAC-759",
            "PAC-759: billing fix.",
            steps,
            config,
            out _);

        Assert.False(ok);
    }

    private static AppRuntimeConfig ConfigWithMcp() =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "en",
            GlobalWikiEnabled = false,
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
                            Name = "zuora-dev",
                            Command = "npx",
                            Enabled = true
                        }
                    ]
                }
            }
        };

    private static AppRuntimeConfig ConfigWithWiki() =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "pt",
            GlobalWikiEnabled = true,
            Agentic = new AgenticConfig { Enabled = true }
        };
}
