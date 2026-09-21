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
            Successful("wiki_search", """{"query":"q1"}"""),
            Successful("wiki_search", """{"query":"q2"}"""),
            Successful("wiki_grep", """{"pattern":"p1"}"""),
            Successful("wiki_search", """{"query":"q3"}"""),
            Successful("wiki_grep", """{"pattern":"p2"}""")
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
    public void Allows_FifthDistinctWikiCall_WithinBudget()
    {
        var config = Config();
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"q1"}"""),
            Successful("wiki_search", """{"query":"q2"}"""),
            Successful("wiki_grep", """{"pattern":"p1"}"""),
            Successful("wiki_search", """{"query":"q3"}""")
        };

        Assert.False(AgenticDuplicateToolCallGuard.TryReject(
            "wiki_grep",
            """{"pattern":"p2"}""",
            steps,
            config,
            out _));
    }

    [Fact]
    public void Rejects_IdenticalWikiQuery_EvenWhenBudgetRemains()
    {
        var config = Config();
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"paccar ITD"}"""),
            Successful("wiki_search", """{"query":"other"}""")
        };

        Assert.True(AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"paccar ITD"}""",
            steps,
            config,
            out var feedback));
        Assert.Contains("já teve sucesso", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_WikiAfterBudgetExhausted_WithoutEvidence_SuggestsMcp()
    {
        var config = Config(withMcp: true);
        var steps = new List<AgentExecutionStep>
        {
            Failed("wiki_search", """{"query":"a"}""", "no hits"),
            Failed("wiki_grep", """{"pattern":"a"}""", "no hits"),
            Failed("wiki_search", """{"query":"b"}""", "no hits"),
            Failed("wiki_grep", """{"pattern":"b"}""", "no hits"),
            Failed("wiki_search", """{"query":"c"}""", "no hits")
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "wiki_search",
            """{"query":"d"}""",
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
    public void ShouldForceAnswerAfterWikiBudget_AfterFirstBudgetRejection()
    {
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"paccar"}"""),
            new()
            {
                Iteration = 2,
                ToolName = "wiki_grep",
                Arguments = """{"pattern":"ITD"}""",
                Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = AgenticDuplicateToolCallGuard.DuplicateRejectedSummary
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
    public void Rejects_ThirdIdenticalFailedNonWikiToolCall()
    {
        var args = """{"objectType":"Subscription","filter":"bad"}""";
        var steps = new List<AgentExecutionStep>
        {
            Failed("zuora__query_objects", args, "invalid filter"),
            Failed("zuora__query_objects", args, "invalid filter")
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            "zuora__query_objects",
            args,
            steps,
            Config(withMcp: true),
            out var feedback);

        Assert.True(rejected);
        Assert.True(AgenticDuplicateToolCallGuard.FeedbackIndicatesDuplicateAfterFailure(feedback));
    }

    [Fact]
    public void Rejects_IdenticalFailedDiscoveryCall()
    {
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 2,
                ToolName = SessionDiscoveryTools.ArtifactRead,
                Arguments = """{"artifact_id":"tool:wiki_search:a689673a"}""",
                Output = "artifact_read requires artifactId.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            }
        };

        var rejected = AgenticDuplicateToolCallGuard.TryReject(
            SessionDiscoveryTools.ArtifactRead,
            """{"artifact_id":"tool:wiki_search:a689673a"}""",
            steps,
            Config(),
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("já falhou", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepeatedFailedDiscoveryCall_ForcesAnswerWhenEvidenceExists()
    {
        var arguments = """{"artifact_id":"tool:wiki_search:a689673a"}""";
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"paccar rules"}""", "PACCAR evidence"),
            new()
            {
                Iteration = 2,
                ToolName = SessionDiscoveryTools.ArtifactRead,
                Arguments = arguments,
                Output = "artifact_read requires artifactId.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero
            },
            new()
            {
                Iteration = 3,
                ToolName = SessionDiscoveryTools.ArtifactRead,
                Arguments = arguments,
                Output = "Rejected: identical artifact_read already failed.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = AgenticDuplicateToolCallGuard.DuplicateAfterFailureSummary
            }
        };

        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswerAfterRepeatedToolFailure(steps));
        Assert.True(AgenticDuplicateToolCallGuard.ShouldForceAnswer(steps));
        Assert.Contains(
            "falhou repetidamente",
            AgenticDuplicateToolCallGuard.BuildForceAnswerNudge(Config(), steps),
            StringComparison.OrdinalIgnoreCase);
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
    public void IsHarnessPolicyRejection_CoversDuplicateAndBudgetSummaries()
    {
        Assert.True(AgenticDuplicateToolCallGuard.IsHarnessPolicyRejection(new AgentExecutionStep
        {
            Iteration = 2,
            ToolName = "wiki_search",
            Arguments = """{"query":"x"}""",
            Output = "Rejected: identical…",
            ExitCode = 1,
            Success = false,
            Summary = AgenticDuplicateToolCallGuard.DuplicateAfterSuccessSummary
        }));

        Assert.True(AgenticDuplicateToolCallGuard.IsHarnessPolicyRejection(new AgentExecutionStep
        {
            Iteration = 3,
            ToolName = "wiki_search",
            Arguments = """{"query":"y"}""",
            Output = "Rejected: wiki_search/wiki_grep budget exhausted this turn.",
            ExitCode = 1,
            Success = false,
            Summary = AgenticDuplicateToolCallGuard.DuplicateRejectedSummary
        }));

        Assert.False(AgenticDuplicateToolCallGuard.IsHarnessPolicyRejection(new AgentExecutionStep
        {
            Iteration = 1,
            ToolName = "zuora__query_objects",
            Arguments = "{}",
            Output = "connection refused",
            ExitCode = 1,
            Success = false
        }));
    }

    [Fact]
    public void ShouldAcceptForceAnswerDespiteValidation_RequiresEvidenceAndNonEmptyAnswer()
    {
        var withEvidence = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"x"}""", "Regras PACCAR ITD.")
        };

        Assert.True(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            finalAnswer: "Resumo a partir da wiki.",
            withEvidence));

        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: false,
            finalAnswer: "Resumo a partir da wiki.",
            withEvidence));

        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            finalAnswer: "   ",
            withEvidence));

        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            finalAnswer: "Resumo",
            steps: []));
    }

    [Fact]
    public void ShouldAcceptForceAnswerDespiteValidation_RejectsMechanicsEchoAndToolLeak()
    {
        var withEvidence = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"x"}""", "Regras PACCAR ITD.")
        };

        const string meta =
            "A ferramenta wiki_search foi chamada mais de uma vez. "
            + "Isso é um limite de orçamento de chamadas. Como corrigir: não chamar a mesma.";

        Assert.True(AgenticDuplicateToolCallGuard.IsGuardrailMechanicsEcho(meta));
        Assert.False(AgenticDuplicateToolCallGuard.IsUsableForceAnswer(meta));
        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            finalAnswer: meta,
            withEvidence));

        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            finalAnswer: "Consultei wiki_search e encontrei as regras.",
            withEvidence));
    }

    [Fact]
    public void ShouldAcceptForceAnswerDespiteValidation_RejectsRepeatedSections()
    {
        var withEvidence = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"x"}""", "Regras PACCAR ITD.")
        };
        const string sentence =
            "The API validates every required field before creating the PACCAR subscription.";
        var repeated = $"{sentence} Other rules are described here. {sentence}";

        Assert.False(AgenticDuplicateToolCallGuard.ShouldAcceptForceAnswerDespiteValidation(
            forceAnswerOnly: true,
            repeated,
            withEvidence));
    }

    [Fact]
    public void TryBuildEvidenceFallbackAnswer_ReturnsSuccessfulOutputs()
    {
        var steps = new List<AgentExecutionStep>
        {
            Successful("wiki_search", """{"query":"x"}""", "Business rules for PACCAR ITD validation.")
        };

        var fallback = AgenticDuplicateToolCallGuard.TryBuildEvidenceFallbackAnswer(Config(), steps);

        Assert.NotNull(fallback);
        Assert.Contains("PACCAR", fallback, StringComparison.Ordinal);
        Assert.Contains("Segue o que foi encontrado", fallback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Como corrigir", fallback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFailureFallbackAnswer_ReportsLastRealFailureWithoutHarnessMeta()
    {
        var steps = new List<AgentExecutionStep>
        {
            Failed("zuora__query_objects", "{}", "Invalid filter syntax."),
            new()
            {
                Iteration = 3,
                ToolName = "zuora__query_objects",
                Arguments = "{}",
                Output = "Rejected: identical call already failed.",
                ExitCode = 1,
                Success = false,
                Duration = TimeSpan.Zero,
                Summary = AgenticDuplicateToolCallGuard.DuplicateAfterFailureSummary
            }
        };

        var fallback = AgenticDuplicateToolCallGuard.BuildFailureFallbackAnswer(Config(), steps);

        Assert.Contains("Não foi possível", fallback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invalid filter syntax", fallback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identical", fallback, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Signature_NormalizesJsonPropertyOrderAndSnakeCase()
    {
        var snake = AgenticDuplicateToolCallGuard.BuildSignature(
            SessionDiscoveryTools.ArtifactTail,
            """{"artifact_id":"TOOL:WIKI_SEARCH:ABC","max_chars":2000}""");
        var camel = AgenticDuplicateToolCallGuard.BuildSignature(
            SessionDiscoveryTools.ArtifactTail,
            """{"maxChars":2000,"artifactId":"tool:wiki_search:abc"}""");

        Assert.Equal(snake, camel);
    }

    private static AgentExecutionStep Successful(string tool, string args, string output = "ok") =>
        new()
        {
            Iteration = 1,
            ToolName = tool,
            Arguments = args,
            Output = output,
            ExitCode = 0,
            Success = true,
            Duration = TimeSpan.FromMilliseconds(5)
        };

    private static AgentExecutionStep Failed(string tool, string args, string output) =>
        new()
        {
            Iteration = 1,
            ToolName = tool,
            Arguments = args,
            Output = output,
            ExitCode = 1,
            Success = false,
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
