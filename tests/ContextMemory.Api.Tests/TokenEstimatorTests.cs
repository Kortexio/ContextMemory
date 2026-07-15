using ContextMemory.Core.Models;
using ContextMemory.Core.Utilities;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class TokenEstimatorTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]
    [InlineData("abcdefgh", 2)]
    public void Estimate_Text_ReturnsExpected(string? text, int expected) =>
        Assert.Equal(expected, TokenEstimator.Estimate(text));

    [Fact]
    public void Estimate_Messages_SumsContentLengths()
    {
        var messages = new[]
        {
            new OllamaMessage { Role = "user", Content = "abcd" },
            new OllamaMessage { Role = "assistant", Content = "abcdefgh" }
        };

        Assert.Equal(3, TokenEstimator.Estimate(messages));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 2)]
    public void EstimateFromByteLength_ReturnsExpected(long bytes, int expected) =>
        Assert.Equal(expected, TokenEstimator.EstimateFromByteLength(bytes));
}
