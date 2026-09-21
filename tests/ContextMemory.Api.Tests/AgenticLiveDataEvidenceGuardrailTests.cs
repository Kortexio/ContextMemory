using System.Text.Json;
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
            LiveDataConfigJson(),
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
            LiveDataConfigJson(),
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
            LiveDataConfigJson(),
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
            LiveDataConfigJson(),
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
            LiveDataConfigJson(),
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Accepts_HonestUnknown_AfterFailedEvidenceTool()
    {
        var config = ConfigWithWiki();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"PAC-759"}""",
                Output = "timeout",
                Success = false,
                ExitCode = 1
            }
        };

        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "busque os tickets PAC-759",
            "Não encontrei o ticket PAC-759 na wiki; a pesquisa falhou.",
            steps,
            LiveDataConfigJson(),
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void StillRejects_InventedAnswer_AfterFailedEvidenceTool()
    {
        var config = ConfigWithWiki();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"PAC-759"}""",
                Output = "timeout",
                Success = false,
                ExitCode = 1
            }
        };

        var ok = AgenticLiveDataEvidenceGuardrail.TryGetRejectionFeedback(
            "busque os tickets PAC-759",
            "PAC-759 is about billing reconciliation and was closed yesterday.",
            steps,
            LiveDataConfigJson(),
            config,
            out _);

        Assert.True(ok);
    }

    private static string LiveDataConfigJson() =>
        JsonSerializer.Serialize(new
        {
            kind = AgenticGuardrailKinds.LiveDataEvidence,
            feedback =
                "Rejected: live-data/wiki question without successful MCP/wiki evidence. Emit tool_calls now — "
                + "prefer wiki_search for tickets/docs or a configured MCP tool (server__tool). "
                + "Example: {\"tool\":\"wiki_search\",\"arguments\":{\"query\":\"TICKET-123\"}}",
            liveDataMarkers = new[]
            {
                "account", "conta", "subscription", "assinatura", "invoice", "fatura", "payment", "pagamento",
                "billing", "canceled", "cancelled", "cancelad", "customer", "cliente", "balance", "saldo",
                "rate plan", "query_objects", "accountnumber", "account number", "ticket", "tickets", "jira",
                "issue", "issues", "confluence", "wiki"
            },
            evidenceToolMarkers = new[]
            {
                "__", "query_objects", "get_account", "manage_customer",
                "wiki_search", "wiki_grep", "wiki_get", "wiki_read"
            },
            honestUnknownMarkers = new[]
            {
                "não encontrei", "nao encontrei", "não foi possível", "nao foi possivel", "sem resultados",
                "sem evidência", "sem evidencia", "não há dados", "nao ha dados", "not found", "no results",
                "no evidence", "could not find", "couldn't find", "unable to find", "no matching", "empty result",
                "tool failed", "tool error", "falhou", "failed"
            }
        });

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
