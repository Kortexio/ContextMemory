using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
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
    public void EnsureCatalog_ReplacesWhenToolsChange()
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "base" },
            new() { Role = "user", Content = "q" }
        };
        var tools1 = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", "Search wiki", new { type = "object" }))
        };
        var tools2 = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", "Search wiki", new { type = "object" })),
            new("function", new OllamaFunction(
                "zuora__query_objects",
                "Query Zuora",
                new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["object"] = new { type = "string" }
                    }
                }))
        };

        ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, tools1);
        ClientSideToolCalling.EnsureCatalogInSystemPrompt(messages, tools2);

        var system = messages[0].Content!;
        Assert.Equal(1, system.Split(ClientSideToolCalling.CatalogMarker, StringSplitOptions.None).Length - 1);
        Assert.Contains("MCP `zuora`", system, StringComparison.Ordinal);
        Assert.Contains("`query_objects`", system, StringComparison.Ordinal);
        Assert.DoesNotContain("`zuora__query_objects`", system, StringComparison.Ordinal);
        Assert.Contains("params:", system, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCatalog_GroupsMcpUnderServerHeader()
    {
        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", "Search wiki", new { type = "object" })),
            new("function", new OllamaFunction(
                "zuora-developer-mcp-PACCAR-ACCP__ask_zuora",
                "Expert on Zuora billing APIs and account lookups for support",
                McpPinnedToolFactory.OpenStubParameters())),
            new("function", new OllamaFunction(
                "zuora-developer-mcp-PACCAR-ACCP__query_objects",
                "Query any Zuora object with ZOQL-like filters",
                McpPinnedToolFactory.OpenStubParameters()))
        };

        var catalog = ClientSideToolCalling.BuildCatalog(tools);
        Assert.Contains("`wiki_search`", catalog, StringComparison.Ordinal);
        Assert.Contains("MCP `zuora-developer-mcp-PACCAR-ACCP`", catalog, StringComparison.Ordinal);
        Assert.Contains("`ask_zuora`", catalog, StringComparison.Ordinal);
        Assert.Contains("`query_objects`", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("`zuora-developer-mcp-PACCAR-ACCP__ask_zuora`", catalog, StringComparison.Ordinal);
        // Descriptions truncated for token economy
        Assert.DoesNotContain("tool_describe for full schema", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatToolNamesSummary_GroupsMcpShortNames()
    {
        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", "w", new { type = "object" })),
            new("function", new OllamaFunction(
                "zuora-dev__ask_zuora", "a", McpPinnedToolFactory.OpenStubParameters())),
            new("function", new OllamaFunction(
                "zuora-dev__query_objects", "q", McpPinnedToolFactory.OpenStubParameters()))
        };

        var summary = ClientSideToolCalling.FormatToolNamesSummary(tools);
        Assert.Equal("wiki_search | zuora-dev: ask_zuora, query_objects", summary);
    }

    [Fact]
    public void TryExpandShortMcpName_ResolvesUniqueShortName()
    {
        var catalog = new[]
        {
            "wiki_search",
            "zuora-dev__ask_zuora",
            "zuora-dev__query_objects"
        };

        Assert.Equal(
            "zuora-dev__ask_zuora",
            ClientSideToolCalling.TryExpandShortMcpName("ask_zuora", catalog));
        Assert.Null(ClientSideToolCalling.TryExpandShortMcpName("missing", catalog));
    }

    [Fact]
    public void ResolveShortMcpName_ReportsAmbiguousCandidates()
    {
        var catalog = new[]
        {
            "zuora-a__get_account",
            "zuora-b__get_account",
            "wiki_search"
        };

        var resolved = ClientSideToolCalling.ResolveShortMcpName("get_account", catalog);
        Assert.Equal(ShortMcpNameKind.Ambiguous, resolved.Kind);
        Assert.Null(resolved.ResolvedName);
        Assert.Equal(2, resolved.Candidates.Count);
        Assert.Null(ClientSideToolCalling.TryExpandShortMcpName("get_account", catalog));
    }

    [Fact]
    public void BuildCatalog_SkipsParamsForOpenStubSchemas()
    {
        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction(
                "open_stub",
                "Open schema tool",
                new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>(),
                    ["additionalProperties"] = true
                })),
            new("function", new OllamaFunction(
                "real_schema",
                "Has real params",
                new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["q"] = new { type = "string" }
                    }
                }))
        };

        var catalog = ClientSideToolCalling.BuildCatalog(tools);
        Assert.Contains("`open_stub`", catalog, StringComparison.Ordinal);
        Assert.Contains("`real_schema`", catalog, StringComparison.Ordinal);
        var openIdx = catalog.IndexOf("`open_stub`", StringComparison.Ordinal);
        var realIdx = catalog.IndexOf("`real_schema`", StringComparison.Ordinal);
        Assert.True(openIdx >= 0 && realIdx > openIdx);
        Assert.DoesNotContain("params:", catalog[openIdx..realIdx], StringComparison.Ordinal);
        Assert.Contains("params:", catalog[realIdx..], StringComparison.Ordinal);
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
