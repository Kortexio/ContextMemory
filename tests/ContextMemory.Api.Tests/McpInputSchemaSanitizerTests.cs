using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic.Mcp;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class McpInputSchemaSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesAdditionalPropertiesFalse()
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "operation" },
            properties = new
            {
                operation = new { type = "string", description = "op" },
                nested = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new { id = new { type = "string" } }
                }
            }
        };

        var sanitized = McpInputSchemaSanitizer.Sanitize(schema);
        var json = JsonSerializer.Serialize(sanitized);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("additionalProperties", out _));
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("properties").TryGetProperty("operation", out _));
    }

    [Fact]
    public void Sanitize_KeepsAdditionalPropertiesTrue()
    {
        var schema = new { type = "object", additionalProperties = true, properties = new { } };
        var json = JsonSerializer.Serialize(McpInputSchemaSanitizer.Sanitize(schema));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Sanitize_CollapsesHugeSchemas()
    {
        var props = new JsonObject();
        for (var i = 0; i < 40; i++)
        {
            props[$"field{i}"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = new string('x', 400),
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["a"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["b"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["c"] = new JsonObject { ["type"] = "string", ["description"] = new string('y', 300) }
                                }
                            }
                        }
                    }
                }
            };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = props
        };

        var json = JsonSerializer.Serialize(McpInputSchemaSanitizer.Sanitize(schema));
        Assert.True(json.Length < 6000, $"expected collapsed schema, got {json.Length} chars");
        Assert.DoesNotContain("additionalProperties\":false", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("properties").EnumerateObject().Count() <= 24);
    }

    [Fact]
    public void Sanitize_Null_ReturnsMinimalObject()
    {
        var json = JsonSerializer.Serialize(McpInputSchemaSanitizer.Sanitize(null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
    }
}
