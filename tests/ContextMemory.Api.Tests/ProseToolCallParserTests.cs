using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ProseToolCallParserTests
{
    [Fact]
    public void TryParse_PromotesNarratedMcpToolJson()
    {
        const string prose = """
            {
                "tool": "zuora-developer-mcp-PACCAR-ACCP__query_objects",
                "arguments": {
                    "object_type": "Account",
                    "filters": [
                        { "field_name": "status", "operator": "=", "value": "Canceled" }
                    ],
                    "limit": 1,
                    "fields_to_return": ["accountNumber", "status"]
                },
                "description": "Querying Zuora"
            }
            """;

        var parsed = ProseToolCallParser.TryParse(prose);
        Assert.NotNull(parsed);
        Assert.Single(parsed!);
        Assert.Equal("zuora-developer-mcp-PACCAR-ACCP__query_objects", parsed[0].Function.Name);
        Assert.Contains("object_type", parsed[0].Function.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_PromotesFencedJson()
    {
        const string prose = """
            Here is the call:
            ```json
            {"tool":"wiki_search","arguments":{"query":"billing"}}
            ```
            """;

        var parsed = ProseToolCallParser.TryParse(prose);
        Assert.NotNull(parsed);
        Assert.Equal("wiki_search", parsed![0].Function.Name);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForPlainAnswer()
    {
        Assert.Null(ProseToolCallParser.TryParse("I cannot find a canceled account without an ID."));
    }

    [Fact]
    public void TryParse_PromotesXmlToolCall()
    {
        const string prose = """
            <tool_call>
            <function=wiki_search>
            <parameter name="query">billing</parameter>
            </function>
            </tool_call>
            """;

        var parsed = ProseToolCallParser.TryParse(prose);
        Assert.NotNull(parsed);
        Assert.Equal("wiki_search", parsed![0].Function.Name);
        Assert.Contains("billing", parsed[0].Function.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_PromotesInvokeAndCallWith()
    {
        var invoke = ProseToolCallParser.TryParse(
            """invoke wiki_search ({"query":"x"})""");
        Assert.NotNull(invoke);
        Assert.Equal("wiki_search", invoke![0].Function.Name);

        var callWith = ProseToolCallParser.TryParse(
            """call zuora__query_objects with {"objectType":"account"}""");
        Assert.NotNull(callWith);
        Assert.Equal("zuora__query_objects", callWith![0].Function.Name);
    }

    [Fact]
    public void Normalize_RewritesQueryObjectsAliases()
    {
        const string raw = """
            {
              "object_type": "Account",
              "filters": [ { "field_name": "status", "operator": "=", "value": "Canceled" } ],
              "limit": 1,
              "fields_to_return": ["accountNumber", "status"]
            }
            """;

        var normalized = McpQueryObjectsArgumentNormalizer.Normalize(
            "zuora-developer-mcp-PACCAR-ACCP__query_objects",
            raw);

        Assert.Contains("\"objectType\":\"account\"", normalized, StringComparison.Ordinal);
        Assert.Contains("status.EQ:Canceled", normalized, StringComparison.Ordinal);
        Assert.Contains("\"pageSize\":1", normalized, StringComparison.Ordinal);
        Assert.Contains("accountNumber", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("object_type", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RewritesFieldsToReturnCamelCase()
    {
        const string raw = """
            {
              "objectType": "account",
              "filter": ["status.EQ:Canceled"],
              "pageSize": 1,
              "fieldsToReturn": ["accountNumber", "status"]
            }
            """;

        var normalized = McpQueryObjectsArgumentNormalizer.Normalize(
            "zuora-developer-mcp-PACCAR-ACCP__query_objects",
            raw);

        Assert.Contains("\"fields\":[", normalized, StringComparison.Ordinal);
        Assert.Contains("accountNumber", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("fieldsToReturn", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RewritesSqlStyleFilterStrings()
    {
        const string raw = """
            { "objectType": "account", "filter": ["status = 'Canceled'"], "pageSize": 1 }
            """;
        var normalized = McpQueryObjectsArgumentNormalizer.Normalize("x__query_objects", raw);
        Assert.Contains("status.EQ:Canceled", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("status =", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterAgainstCatalog_KeepsKnownTools_CapsAndDropsUnknown()
    {
        var catalog = new List<OllamaTool>
        {
            new("function", new OllamaFunction("wiki_search", null, null)),
            new("function", new OllamaFunction("canvas_upsert", null, null))
        };

        var raw = new List<OllamaToolCall>
        {
            new(new OllamaFunctionCall("wiki_search", """{"query":"a"}""")),
            new(new OllamaFunctionCall("invented_tool", """{"x":1}""")),
            new(new OllamaFunctionCall("canvas_upsert", """{"id":"1"}""")),
            new(new OllamaFunctionCall("wiki_search", """{"query":"b"}""")),
            new(new OllamaFunctionCall("wiki_search", "not-json")),
            new(new OllamaFunctionCall("canvas_upsert", """{"id":"2"}""")),
            new(new OllamaFunctionCall("wiki_search", """{"query":"c"}"""))
        };

        var filtered = ProseToolCallParser.FilterAgainstCatalog(
            raw,
            catalog,
            maxPerTurn: 2,
            out var droppedUnknown,
            out var droppedInvalidArgs,
            out var droppedCapped);

        Assert.NotNull(filtered);
        Assert.Equal(2, filtered!.Count);
        Assert.Equal(1, droppedUnknown);
        Assert.Equal(1, droppedInvalidArgs);
        Assert.Equal(3, droppedCapped);
        Assert.Equal("wiki_search", filtered[0].Function.Name);
        Assert.Equal("canvas_upsert", filtered[1].Function.Name);
    }

    [Fact]
    public void FilterAgainstCatalog_ReturnsNull_WhenCatalogEmpty()
    {
        var raw = new List<OllamaToolCall>
        {
            new(new OllamaFunctionCall("wiki_search", """{"query":"a"}"""))
        };

        var filtered = ProseToolCallParser.FilterAgainstCatalog(
            raw,
            catalog: null,
            maxPerTurn: 6,
            out var droppedUnknown,
            out _,
            out _);

        Assert.Null(filtered);
        Assert.Equal(1, droppedUnknown);
    }
}
