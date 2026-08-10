using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ClientSideToolCallingTests
{
    [Fact]
    public void Flatten_ConvertsToolCallsAndToolRoles()
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "sys" },
            new() { Role = "user", Content = "hi" },
            new()
            {
                Role = "assistant",
                Content = "",
                ToolCalls =
                [
                    new OllamaToolCall(new OllamaFunctionCall("wiki_search", """{"query":"x"}"""))
                ]
            },
            new() { Role = "tool", Content = """{"ok":true,"html":"<b>hi</b>"}""" }
        };

        var flat = ClientSideToolCalling.FlattenForClientSideWire(messages);
        Assert.Equal(4, flat.Count);
        Assert.Equal("assistant", flat[2].Role);
        Assert.Null(flat[2].ToolCalls);
        Assert.Contains("wiki_search", flat[2].Content, StringComparison.Ordinal);
        Assert.Equal("user", flat[3].Role);
        Assert.StartsWith("Tool result:", flat[3].Content);
        Assert.DoesNotContain("<b>", flat[3].Content, StringComparison.Ordinal);
        Assert.Contains("(b)", flat[3].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureCatalog_AppendsOnce()
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "base" },
            new() { Role = "user", Content = "q" }
        };
        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", "Search wiki", new { type = "object" }))
        };

        ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, tools);
        ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, tools);

        var system = messages[0].Content;
        Assert.Contains(ClientSideToolCalling.CatalogMarker, system, StringComparison.Ordinal);
        Assert.Equal(1, system.Split(ClientSideToolCalling.CatalogMarker, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("<function", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<parameter", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<tool_call", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("""{"tool":""", system, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCatalog_NeutralizesAngleBracketsInDescriptions()
    {
        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction(
                "query_objects",
                "Filter <Account> where status is Canceled",
                new { type = "object" }))
        };

        var catalog = ClientSideToolCalling.BuildCatalog(tools);
        Assert.DoesNotContain("<Account>", catalog, StringComparison.Ordinal);
        Assert.Contains("(Account)", catalog, StringComparison.Ordinal);
    }
}
