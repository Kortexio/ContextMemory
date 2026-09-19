using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using ContextMemory.Core.Utilities;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgentContextCompactorTests
{
    [Fact]
    public void ShrinkToBudget_NoOpWhenUnderBudget()
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "short" },
            new() { Role = "user", Content = "hi" }
        };

        AgentContextCompactor.ShrinkToBudget(messages, 4096);

        Assert.Equal(2, messages.Count);
        Assert.Equal("short", messages[0].Content);
        Assert.Equal("hi", messages[1].Content);
    }

    [Fact]
    public void ShrinkToBudget_DropsHistoryAndFits4096Window()
    {
        var system = new string('S', 12_000);
        var history = new string('H', 8_000);
        var user = "What is the invoice total?";
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "assistant", Content = history },
            new() { Role = "user", Content = user }
        };

        Assert.True(TokenEstimator.Estimate(messages) > 4096);

        AgentContextCompactor.ShrinkToBudget(messages, 3072);

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal(user, messages[1].Content);
        Assert.True(TokenEstimator.Estimate(messages) <= 3072);
        Assert.Contains("S", messages[0].Content);
    }
}
