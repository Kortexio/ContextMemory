using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticDuplicateToolCallGuardTests
{
    [Fact]
    public void Rejects_IdenticalWikiSearch_IncludingUnicodeEscapeVariant()
    {
        var config = Config();
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"regras criação subscrição paccar"}""")
        };

        var again = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"regras cria\u00E7\u00E3o subscri\u00E7\u00E3o paccar"}""",
            steps,
            config,
            out var feedback);

        Assert.True(again);
        Assert.Contains("NÃO repitas", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP", feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_DifferentWikiQuery()
    {
        var config = Config();
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"regras criação subscrição paccar"}""")
        };

        var ok = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"PACCAR subscription create rules Zuora"}""",
            steps,
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Rejects_EmptyWikiSearchQuery_Immediately()
    {
        var config = Config(withMcp: true);

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            "{}",
            steps: [],
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("não vazio", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask_zuora", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_RetryAfterIdenticalFailedWikiSearch()
    {
        var config = Config(withMcp: true);
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = "{}",
                Output = "wiki_search requires a non-empty \"query\".",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            }
        };

        // Empty args are rejected before looking at steps; also cover non-empty identical fail.
        var emptyAgain = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search", "{}", steps, config, out _);
        Assert.True(emptyAgain);

        var failedOnce = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"same"}""",
                Output = "error",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            }
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"same"}""",
            failedOnce,
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("já falhou", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask_zuora", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_WikiAfterBudgetExhausted_WithEvidence_ForcesAnswerNotMcp()
    {
        var config = Config(withMcp: true);
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = "{}",
                Output = "rejected empty",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            },
            new()
            {
                Iteration = 2,
                ToolName = "wiki_search",
                Arguments = """{"query":"paccar"}""",
                Output = "Found 5 match(es)",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(2)
            }
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"subscription rules"}""",
            steps,
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("esgotado", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Responde AGORA", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ask_zuora", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_WikiAfterBudgetExhausted_WithoutEvidence_SuggestsMcp()
    {
        var config = Config(withMcp: true);
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"a"}""",
                Output = "no hits",
                ExitCode = 0,
                Success = false,
                Duration = TimeSpan.FromMilliseconds(2)
            },
            new()
            {
                Iteration = 2,
                ToolName = "wiki_grep",
                Arguments = """{"pattern":"a"}""",
                Output = "no hits",
                ExitCode = 0,
                Success = false,
                Duration = TimeSpan.FromMilliseconds(2)
            }
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"b"}""",
            steps,
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("esgotado", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask_zuora", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldForceAnswerAfterWikiBudget_AfterConsecutiveRejectionsWithEvidence()
    {
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"paccar"}"""),
            Successful("wiki_grep", """{"pattern":"ITD"}"""),
            new()
            {
                Iteration = 3,
                ToolName = "wiki_search",
                Arguments = """{"query":"x"}""",
                Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = "Duplicate tool call rejected"
            },
            new()
            {
                Iteration = 4,
                ToolName = "wiki_search",
                Arguments = """{"query":"y"}""",
                Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = "Duplicate tool call rejected"
            }
        };

        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswerAfterWikiBudget(steps));
    }

    [Fact]
    public void ShouldForceAnswerAfterWikiBudget_True_EvenWithoutEvidence()
    {
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = "{}",
                Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            },
            new()
            {
                Iteration = 2,
                ToolName = "wiki_search",
                Arguments = "{}",
                Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            }
        };

        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswerAfterWikiBudget(steps));
        var nudge = AgenticDuplicateToolCallGuard.BuildForceAnswerNudge(Config(), steps);
        Assert.Contains("honestidade", nudge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_RetryFailedNonWikiTool()
    {
        var config = Config();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = """{"command":"echo x"}""",
                Output = "error",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            }
        };

        var ok = AgenticDuplicateToolCallGuard.TryReject(
            "shell_execute",
            """{"command":"echo x"}""",
            steps,
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void IdenticalSuccessfulCall_FeedbackForcesAnswerNotMoreTools()
    {
        var config = Config(withMcp: true);
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"subscription rules"}""")
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"subscription rules"}""",
            steps,
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.True(AgenticDuplicateToolCallGuard.FeedbackIndicatesDuplicateAfterSuccess(feedback));
        Assert.Contains("Responde AGORA", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ask_zuora", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldForceAnswerAfterDuplicateSuccess_OnFirstRejection()
    {
        var args = """{"objectType":"subscription","filter":["status.EQ:Cancelled"],"pageSize":5}""";
        var steps = new List<AgentExecutionStep>
        {
            Successful("zuora-dev__query_objects", args),
            new()
            {
                Iteration = 2,
                ToolName = "zuora-dev__query_objects",
                Arguments = args,
                Output =
                    "Rejected: identical `zuora-dev__query_objects` already succeeded — do NOT repeat the same arguments. "
                    + "Answer the user NOW from the tool result already gathered.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = AgenticDuplicateToolCallGuard.DuplicateAfterSuccessSummary
            }
        };

        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswerAfterDuplicateSuccess(steps));
        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswer(steps));
        var nudge = AgenticDuplicateToolCallGuard.BuildForceAnswerNudge(Config(withMcp: true), steps);
        Assert.Contains("já teve sucesso", nudge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_IdenticalSuccessfulMcpQueryObjects()
    {
        var config = Config(withMcp: true);
        var args = """{"objectType":"subscription","filter":["status.EQ:Cancelled"],"pageSize":5}""";
        var steps = new List<AgentExecutionStep>
        {
            Successful("zuora-dev__query_objects", args)
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "zuora-dev__query_objects",
            args,
            steps,
            config,
            out var feedback);

        Assert.True(rejected);
        Assert.True(AgenticDuplicateToolCallGuard.FeedbackIndicatesDuplicateAfterSuccess(feedback));
        Assert.Contains("já teve sucesso", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Responde AGORA", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signature_NormalizesWhitespaceAndCase()
    {
        var a = AgenticDuplicateToolCallGuard.BuildSignature(
            "Wiki_Search",
            """{"query":"  Hello   World "}""");
        var b = AgenticDuplicateToolCallGuard.BuildSignature(
            "wiki_search",
            """{"query":"hello world"}""");

        Assert.Equal(a, b);
    }

    private static AgentExecutionStep Successful(string tool, string args) =>
        new()
        {
            Iteration = 1,
            ToolName = tool,
            Arguments = args,
            Output = "ok",
            ExitCode = 0,
            Success = true,
            Duration = TimeSpan.FromMilliseconds(5)
        };

    private static AppRuntimeConfig Config(bool withMcp = false) =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "pt",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = withMcp
                    ? new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora",
                                Enabled = true,
                                Url = "mock://zuora"
                            }
                        ]
                    }
                    : new AgenticToolsConfig()
            }
        };
}
