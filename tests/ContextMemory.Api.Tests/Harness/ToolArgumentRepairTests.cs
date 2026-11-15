using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ToolArgumentRepairTests
{
    [Fact]
    public void Coerce_NumericString_ToInteger()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                count = new { type = "integer" }
            },
            required = new[] { "count" }
        };

        var result = ToolArgumentRepair.Repair(
            "search",
            """{"count":"42"}""",
            schema,
            activeToolNames: new HashSet<string>(StringComparer.Ordinal) { "search" });

        Assert.True(result.Changed);
        Assert.Contains("coerceNumericString", result.AppliedRules);
        using var doc = JsonDocument.Parse(result.ArgumentsJson);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("count").ValueKind);
        Assert.Equal(42, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Recover_MangledToolName()
    {
        var result = ToolArgumentRepair.Repair(
            "wik_search",
            """{"query":"billing"}""",
            schema: null,
            activeToolNames: new HashSet<string>(StringComparer.Ordinal)
            {
                "wiki_search",
                "wiki_grep"
            });

        Assert.True(result.NameRecovered);
        Assert.Equal("wiki_search", result.ToolName);
        Assert.Contains("recoverMangledToolName", result.AppliedRules);
    }

    [Fact]
    public void Drop_NullProperties()
    {
        var result = ToolArgumentRepair.Repair(
            "search",
            """{"query":"ok","extra":null}""",
            schema: null,
            activeToolNames: null);

        Assert.True(result.Changed);
        Assert.Contains("dropNullOrUndefined", result.AppliedRules);
        var root = JsonNode.Parse(result.ArgumentsJson)!.AsObject();
        Assert.True(root.ContainsKey("query"));
        Assert.False(root.ContainsKey("extra"));
    }
}
