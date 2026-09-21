using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Policies;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticToolCallPolicyChainTests
{
    [Fact]
    public void Chain_RunsPoliciesInOrder_FirstRejectionWins()
    {
        var chain = AgenticToolCallPolicyChain.CreateDefault();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"a"}""",
                Output = "ok",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(1)
            },
            new()
            {
                Iteration = 2,
                ToolName = "wiki_search",
                Arguments = """{"query":"b"}""",
                Output = "ok",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(1)
            }
        };

        var rejected = chain.TryReject(
            new AgenticToolCallPolicyContext(
                "wiki_search",
                """{"query":"c"}""",
                steps,
                new AppRuntimeConfig { AppId = "t", DefaultLanguage = "pt" }),
            out var feedback,
            out var policyName);

        Assert.True(rejected);
        Assert.Equal("wiki-budget", policyName);
        Assert.Contains("esgotado", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredArgumentsPolicy_RejectsShellWithoutCommand()
    {
        var policy = new RequiredArgumentsToolCallPolicy();
        var catalog = new List<OllamaTool>
        {
            new(
                "function",
                new OllamaFunction(
                    "shell_execute",
                    "Run",
                    new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["command"] = new Dictionary<string, object?> { ["type"] = "string" }
                        },
                        ["required"] = new[] { "command" }
                    }))
        };

        var rejected = policy.TryReject(
            new AgenticToolCallPolicyContext(
                "shell_execute",
                "{}",
                [],
                new AppRuntimeConfig { AppId = "t", DefaultLanguage = "en" },
                catalog),
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("command", feedback, StringComparison.OrdinalIgnoreCase);
    }
}
