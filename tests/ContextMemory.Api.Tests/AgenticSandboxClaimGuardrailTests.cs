using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticSandboxClaimGuardrailTests
{
    private static AppRuntimeConfig SelfHostedConfig() => new()
    {
        AppId = "test",
        DefaultLanguage = "pt-PT",
        Agentic = new AgenticConfig
        {
            Enabled = true,
            Tools = new AgenticToolsConfig
            {
                Execution =
                [
                    new ExecutionToolConfig
                    {
                        Type = "self-hosted-sandbox",
                        Runtime = "python",
                        SandboxEndpoint = "http://sandbox-runtime:8080",
                        AllowEgress = true
                    }
                ]
            }
        }
    };

    private static string SandboxConfigJson() =>
        AgenticCatalogSeed.Guardrails.First(g => g.Id == "sandbox-claim-reject").ConfigJson;

    [Fact]
    public void Rejects_AcaIsolationClaim_WhenSelfHosted()
    {
        var answer =
            "The python_execute environment in a managed cloud container session is an isolated sandbox — it has no access to the network.";

        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            answer,
            [],
            SandboxConfigJson(),
            SelfHostedConfig(),
            out var feedback);

        Assert.True(hit);
        Assert.Contains("self-hosted-sandbox", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_calls", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_HypotheticalFailureWithoutToolSteps()
    {
        var answer =
            "What would happen if I tried to run it now: python_execute would fail with DNS/timeout.";

        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            answer,
            [],
            SandboxConfigJson(),
            SelfHostedConfig(),
            out _);

        Assert.True(hit);
    }

    [Fact]
    public void Allows_Answer_WhenNoFalseClaim()
    {
        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            "Here is the billing query result: 3 accounts found.",
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "billing__query_objects",
                    Success = true,
                    ExitCode = 0,
                    Output = "ok",
                    Arguments = "{}",
                    Duration = TimeSpan.FromMilliseconds(10)
                }
            ],
            SandboxConfigJson(),
            SelfHostedConfig(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Allows_Describing_Real_Network_Failure_From_Tool()
    {
        var answer = "python_execute failed: Temporary failure in name resolution when reaching the external network.";
        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            answer,
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "python_execute",
                    Success = false,
                    ExitCode = 1,
                    Output = "Temporary failure in name resolution",
                    Arguments = "{}",
                    Duration = TimeSpan.FromMilliseconds(10)
                }
            ],
            SandboxConfigJson(),
            SelfHostedConfig(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public async Task DeterministicValidator_RejectsFabricatedAcaClaim()
    {
        var validator = new DeterministicAgentValidator();
        var sandboxGuardrail = AgenticCatalogSeed.Guardrails.First(g => g.Id == "sandbox-claim-reject");
        var config = SelfHostedConfig() with
        {
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveGuardrailKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    AgenticGuardrailKinds.SandboxClaim
                },
                ActiveGuardrails = [sandboxGuardrail]
            }
        };
        var result = await validator.ValidateAsync(new AgentValidationRequest
        {
            FinalAnswer =
                "python_execute in a managed cloud container session has no access to the network, so I cannot call the API.",
            Steps = [],
            RuntimeConfig = config
        });

        Assert.False(result.IsValid);
        Assert.Contains("invented false sandbox", result.FeedbackForModel, StringComparison.OrdinalIgnoreCase);
    }
}
