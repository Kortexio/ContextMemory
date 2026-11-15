using ContextMemory.Core.Agentic.Harness;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class PhantomToolUseTests
{
    [Fact]
    public void Detect_AnnouncedToolWithoutCall()
    {
        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wiki_search", "bench-crm__query_objects" };
        var result = PhantomToolUse.Detect(
            "I'll call wiki_search now to find the host.",
            hasToolCalls: false,
            tools);
        Assert.True(result.IsPhantom);
        Assert.Equal("wiki_search", result.MatchedToolName);
    }

    [Fact]
    public void Detect_False_WhenToolCallsPresent()
    {
        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wiki_search" };
        var result = PhantomToolUse.Detect("I'll call wiki_search", hasToolCalls: true, tools);
        Assert.False(result.IsPhantom);
    }
}
