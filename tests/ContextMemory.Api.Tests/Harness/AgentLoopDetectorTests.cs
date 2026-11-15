using ContextMemory.Core.Agentic.Harness;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgentLoopDetectorTests
{
    [Fact]
    public void IdenticalStreak_TriggersShouldStop()
    {
        var det = new AgentLoopDetector(stallThreshold: 3);

        RepeatStep(det, "wiki_search", """{"query":"a"}""", "A");
        Assert.False(det.ShouldStop());

        RepeatStep(det, "wiki_search", """{"query":"a"}""", "A");
        RepeatStep(det, "wiki_search", """{"query":"a"}""", "A");
        Assert.False(det.ShouldStop());

        RepeatStep(det, "wiki_search", """{"query":"a"}""", "A");
        Assert.True(det.ShouldStop());
        Assert.Equal("identical", det.StallKind());
        Assert.Contains("identical", det.Cause(), StringComparison.OrdinalIgnoreCase);
    }

    private static void RepeatStep(
        AgentLoopDetector det,
        string toolName,
        string args,
        string result)
    {
        det.RecordCall(toolName, args, result);
        det.EndStep();
    }
}
