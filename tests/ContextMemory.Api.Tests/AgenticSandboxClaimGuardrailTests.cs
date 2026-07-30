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

    [Fact]
    public void Rejects_AcaIsolationClaim_WhenSelfHosted()
    {
        var answer =
            "O ambiente python_execute no Azure Container Apps é isolado (sandbox) — ele não tem acesso à rede externa.";

        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            answer,
            [],
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
            "O que aconteceria se eu tentasse executá-lo agora: o python_execute falharia por DNS/timeout.";

        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            answer,
            [],
            SelfHostedConfig(),
            out _);

        Assert.True(hit);
    }

    [Fact]
    public void Allows_Answer_WhenNoFalseClaim()
    {
        var hit = AgenticSandboxClaimGuardrail.TryGetRejectionFeedback(
            "Aqui está o resultado da consulta Zuora: 3 contas encontradas.",
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "zuora__ask_zuora",
                    Success = true,
                    ExitCode = 0,
                    Output = "ok",
                    Arguments = "{}",
                    Duration = TimeSpan.FromMilliseconds(10)
                }
            ],
            SelfHostedConfig(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Allows_Describing_Real_Network_Failure_From_Tool()
    {
        var answer = "O python_execute falhou: Temporary failure in name resolution ao aceder à rede externa.";
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
            SelfHostedConfig(),
            out _);

        Assert.False(hit);
    }

    [Fact]
    public async Task DeterministicValidator_RejectsFabricatedAcaClaim()
    {
        var validator = new DeterministicAgentValidator();
        var config = SelfHostedConfig() with
        {
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveGuardrailKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    AgenticGuardrailKinds.SandboxClaim
                },
                ActiveGuardrails =
                [
                    new AgenticGuardrailDefinition
                    {
                        Id = "sandbox-claim-reject",
                        Name = "Reject fabricated sandbox limits",
                        Kind = AgenticGuardrailKinds.SandboxClaim,
                        ConfigJson = "{}"
                    }
                ]
            }
        };
        var result = await validator.ValidateAsync(new AgentValidationRequest
        {
            FinalAnswer =
                "O python_execute no Azure Container Apps não tem acesso à rede externa, por isso não posso chamar a API.",
            Steps = [],
            RuntimeConfig = config
        });

        Assert.False(result.IsValid);
        Assert.Contains("NÃO", result.FeedbackForModel, StringComparison.OrdinalIgnoreCase);
    }
}
