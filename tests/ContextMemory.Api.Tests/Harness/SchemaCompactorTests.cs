using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SchemaCompactorTests
{
    [Fact]
    public void CompactTrue_TruncatesLongDescriptions()
    {
        var longRoot = new string('R', SchemaCompactor.MaxRootDescriptionChars + 40);
        var longProp = new string('P', SchemaCompactor.MaxPropertyDescriptionChars + 40);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["description"] = longRoot,
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = longProp
                }
            }
        };

        var compacted = SchemaCompactor.Compact(schema, compact: true);
        var node = Assert.IsAssignableFrom<JsonNode>(compacted).AsObject();

        var rootDesc = node["description"]!.GetValue<string>();
        Assert.True(rootDesc.Length <= SchemaCompactor.MaxRootDescriptionChars + 1);
        Assert.EndsWith("…", rootDesc, StringComparison.Ordinal);

        var propDesc = node["properties"]!["query"]!["description"]!.GetValue<string>();
        Assert.True(propDesc.Length <= SchemaCompactor.MaxPropertyDescriptionChars + 1);
        Assert.EndsWith("…", propDesc, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactFalse_LeavesDescriptionsIntact()
    {
        var longRoot = new string('R', SchemaCompactor.MaxRootDescriptionChars + 40);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["description"] = longRoot
        };

        var result = SchemaCompactor.Compact(schema, compact: false);
        var node = Assert.IsAssignableFrom<JsonNode>(result).AsObject();
        Assert.Equal(longRoot, node["description"]!.GetValue<string>());
    }
}
