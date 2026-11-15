using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class EvidenceFinishGateTests
{
    [Fact]
    public void DiscoveryOnly_SuccessfulToolDescribe_Refuses()
    {
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = SessionDiscoveryTools.ToolDescribe,
                Arguments = """{"name":"wiki_search"}""",
                Output = "schema ok",
                Success = true
            }
        };

        var decision = EvidenceFinishGate.Evaluate(
            userObjective: "What is the billing rule for PAC?",
            finalAnswer: "Here is the answer from discovery.",
            steps,
            runtimeConfig: new AppRuntimeConfig { AppId = "test" },
            profile: EnabledProfile(),
            finishNudgesAlready: 0);

        Assert.True(decision.Refuse);
        Assert.Equal("discovery-only", decision.Reason);
        Assert.Contains("Discovery-only", decision.FeedbackForModel, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySteps_DoesNotRefuse()
    {
        var decision = EvidenceFinishGate.Evaluate(
            userObjective: "Hello",
            finalAnswer: "Hi there.",
            steps: Array.Empty<AgentExecutionStep>(),
            runtimeConfig: new AppRuntimeConfig { AppId = "test" },
            profile: EnabledProfile(),
            finishNudgesAlready: 0);

        Assert.False(decision.Refuse);
        Assert.Equal("ok", decision.Reason);
        Assert.Null(decision.FeedbackForModel);
    }

    private static ModelHarnessProfile EnabledProfile() =>
        new(
            Mode: ModelHarnessMode.Weak,
            MaxRefunds: 12,
            StallThreshold: 3,
            MaxFinishNudges: 3,
            MaxContinueExtensions: 8,
            AbsoluteMaxIterationsCeiling: ModelHarnessProfile.HardAbsoluteMaxIterations,
            CompactSchemas: true,
            PreferFullSchemas: true,
            EnableEvidenceFinishGate: true,
            SoftenForceAnswerOnly: true,
            FamilyIds: ModelHarnessProfile.FamilyIdsAll);
}
