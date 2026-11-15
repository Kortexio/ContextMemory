using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Compacts JSON Schema for Weak harness so full MCP schemas fit context
/// (keep types + required; strip long property descriptions).
/// </summary>
public static class SchemaCompactor
{
    public const int MaxPropertyDescriptionChars = 80;
    public const int MaxRootDescriptionChars = 120;

    public static object Compact(object? schema, bool compact)
    {
        if (!compact || schema is null)
            return schema ?? new Dictionary<string, object?>();

        try
        {
            var node = schema switch
            {
                JsonNode jn => jn.DeepClone(),
                JsonElement el => JsonNode.Parse(el.GetRawText()),
                string s => JsonNode.Parse(s),
                _ => JsonNode.Parse(JsonSerializer.Serialize(schema))
            };
            if (node is null)
                return schema;

            CompactNode(node, isRoot: true);
            return node;
        }
        catch
        {
            return schema;
        }
    }

    private static void CompactNode(JsonNode node, bool isRoot)
    {
        if (node is not JsonObject obj)
            return;

        if (obj["description"] is JsonValue descVal && descVal.TryGetValue<string>(out var desc))
        {
            var max = isRoot ? MaxRootDescriptionChars : MaxPropertyDescriptionChars;
            if (desc.Length > max)
                obj["description"] = desc[..max] + "…";
        }

        if (obj["properties"] is JsonObject props)
        {
            foreach (var key in props.Select(p => p.Key).ToList())
            {
                if (props[key] is JsonNode child)
                    CompactNode(child, isRoot: false);
            }
        }

        if (obj["items"] is JsonNode items)
            CompactNode(items, isRoot: false);

        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (obj[key] is JsonArray arr)
            {
                foreach (var child in arr)
                {
                    if (child is not null)
                        CompactNode(child, isRoot: false);
                }
            }
        }
    }
}
