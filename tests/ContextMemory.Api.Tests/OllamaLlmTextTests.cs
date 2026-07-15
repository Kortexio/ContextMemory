using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class OllamaLlmTextTests
{
    [Fact]
    public void NormalizeAssistantContent_StripsQwenThinkingBlock()
    {
        var raw = "Thinking Process:\n\n1. Analyze...\n\n**Output:**\nSESSAO-OK";
        Assert.Equal("SESSAO-OK", OllamaLlmText.NormalizeAssistantContent(raw));
    }

    [Fact]
    public void NormalizeAssistantContent_PreservesPlainText()
    {
        Assert.Equal("hello", OllamaLlmText.NormalizeAssistantContent("hello"));
    }
}
