using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticDestructiveActionDetectorTests
{
    [Fact]
    public void Analyze_MatchesKeywordInArguments()
    {
        var toolCall = new OllamaToolCall(
            new OllamaFunctionCall("shell_execute", """{"command":"delete prod database"}"""));

        var match = AgenticDestructiveActionDetector.Analyze(
            toolCall,
            new AgenticGuardrailsConfig { RequireConfirmationFor = ["delete", "deploy-prod"] });

        Assert.NotNull(match);
        Assert.Equal("delete", match!.Keyword);
    }
}

public sealed class AgenticConfirmationParserTests
{
    [Fact]
    public void IsConfirmation_AcceptsExplicitToken()
    {
        Assert.True(AgenticConfirmationParser.IsConfirmation("[CONFIRM:abc123]", "abc123"));
    }

    [Fact]
    public void IsConfirmation_AcceptsNaturalLanguage()
    {
        Assert.True(AgenticConfirmationParser.IsConfirmation("Confirmo a execução", "abc123"));
    }

    [Fact]
    public void IsDismissal_DetectsCancel()
    {
        Assert.True(AgenticConfirmationParser.IsDismissal("Cancelo a operação"));
    }
}

public sealed class AgenticNetworkEgressPolicyTests
{
    [Fact]
    public void IsIntegrationUrlAllowed_Restricted_BlocksExternalWithoutAllowlist()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Guardrails = new AgenticGuardrailsConfig { NetworkEgress = "restricted" }
            }
        };

        var integration = new IntegrationToolConfig
        {
            Name = "zuora",
            Url = "https://external.zuora.example/mcp"
        };

        Assert.False(AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(config, integration));
    }

    [Fact]
    public void IsIntegrationUrlAllowed_Restricted_AllowsMock()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Guardrails = new AgenticGuardrailsConfig { NetworkEgress = "restricted" }
            }
        };

        var integration = new IntegrationToolConfig { Name = "zuora", Url = "mock://zuora" };
        Assert.True(AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(config, integration));
    }

    [Fact]
    public void IsIntegrationUrlAllowed_Restricted_AllowsAllowlistedHost()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Guardrails = new AgenticGuardrailsConfig
                {
                    NetworkEgress = "restricted",
                    AllowedEgressHosts = ["internal.zuora.example"]
                }
            }
        };

        var integration = new IntegrationToolConfig
        {
            Name = "zuora",
            Url = "https://internal.zuora.example/mcp"
        };

        Assert.True(AgenticNetworkEgressPolicy.IsIntegrationUrlAllowed(config, integration));
    }
}

public sealed class AgenticToolRegistryRuntimeTests
{
    [Fact]
    public void BuildExecutionTools_IncludesPythonAndNode()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = new AgenticToolsConfig
                {
                    Execution =
                    [
                        new ExecutionToolConfig { Type = "aca-session", Runtime = "shell", PoolEndpoint = "mock://s" },
                        new ExecutionToolConfig { Type = "aca-session", Runtime = "python", PoolEndpoint = "mock://p" },
                        new ExecutionToolConfig { Type = "aca-session", Runtime = "node", PoolEndpoint = "mock://n" }
                    ]
                }
            }
        };

        var tools = AgenticToolRegistry.BuildExecutionTools(config).Select(t => t.Function.Name).ToList();

        Assert.Contains(AgenticToolRegistry.ShellExecuteToolName, tools);
        Assert.Contains(AgenticToolRegistry.PythonExecuteToolName, tools);
        Assert.Contains(AgenticToolRegistry.NodeExecuteToolName, tools);
    }

    [Fact]
    public void BuildExecutionTools_IncludesCustomContainer()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
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
                            Runtime = "custom",
                            PoolEndpoint = "mock://c",
                            ContainerImage = "myregistry.io/tools:1.0"
                        }
                    ]
                }
            }
        };

        var tools = AgenticToolRegistry.BuildExecutionTools(config).Select(t => t.Function.Name).ToList();
        Assert.Contains(AgenticToolRegistry.ContainerExecuteToolName, tools);
    }
}

public sealed class DeterministicAgentValidatorExtendedTests
{
    [Fact]
    public async Task ValidateAsync_RejectsWhenExpectedPatternMissing()
    {
        var validator = new DeterministicAgentValidator();
        var result = await validator.ValidateAsync(new AgentValidationRequest
        {
            FinalAnswer = "Resposta sem formato",
            Steps = [],
            RuntimeConfig = new AppRuntimeConfig
            {
                AppId = "test",
                Agentic = new AgenticConfig
                {
                    Guardrails = new AgenticGuardrailsConfig
                    {
                        ExpectedAnswerPatterns = ["^## Resumo"]
                    }
                }
            }
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsFailedToolsWhenRequireZeroExitCode()
    {
        var validator = new DeterministicAgentValidator();
        var result = await validator.ValidateAsync(new AgentValidationRequest
        {
            FinalAnswer = "Tudo correu bem aparentemente.",
            Steps =
            [
                new AgentExecutionStep
                {
                    Iteration = 1,
                    ToolName = "shell_execute",
                    Arguments = "{}",
                    Output = "fail",
                    ExitCode = 1,
                    Success = false,
                    Duration = TimeSpan.Zero
                }
            ],
            RuntimeConfig = new AppRuntimeConfig
            {
                AppId = "test",
                Agentic = new AgenticConfig
                {
                    Guardrails = new AgenticGuardrailsConfig { RequireZeroExitCode = true }
                }
            }
        });

        Assert.False(result.IsValid);
    }
}

public sealed class AgenticMaxIterationsMetadataTests
{
    [Fact]
    public void FromResult_MaxIterationsPending_IncludesReviewLabel()
    {
        var meta = AgenticStreamMetadata.FromResult(
            AgentResult.AwaitingHumanConfirmation(
                "Human review required",
                "rev1",
                [],
                5,
                AgenticPendingKinds.MaxIterations));

        Assert.True(meta.AwaitingConfirmation);
        Assert.Contains("Human review", meta.Label, StringComparison.OrdinalIgnoreCase);
    }
}
