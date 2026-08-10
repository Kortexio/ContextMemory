using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
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
    public void Normalize_WrapsObjectOrStringFilterAsStringArray()
    {
        const string asObject = """
            { "objectType": "account", "filter": { "field": "status", "operator": "=", "value": "Canceled" }, "pageSize": 1 }
            """;
        var n1 = McpQueryObjectsArgumentNormalizer.Normalize("x__query_objects", asObject);
        Assert.Contains("status.EQ:Canceled", n1, StringComparison.Ordinal);

        const string asString = """
            { "objectType": "account", "filter": "status.EQ:Canceled", "pageSize": 1 }
            """;
        var n2 = McpQueryObjectsArgumentNormalizer.Normalize("x__query_objects", asString);
        Assert.Contains("\"filter\":[\"status.EQ:Canceled\"]", n2, StringComparison.Ordinal);
    }
}
