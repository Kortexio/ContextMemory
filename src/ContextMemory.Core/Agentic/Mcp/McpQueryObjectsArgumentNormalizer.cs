using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// Rewrites common LLM-hallucinated <c>query_objects</c> argument shapes into the Zuora MCP schema.
/// </summary>
public static class McpQueryObjectsArgumentNormalizer
{
    public static string Normalize(string toolName, string argumentsJson)
    {
        if (!toolName.EndsWith("query_objects", StringComparison.OrdinalIgnoreCase)
            && !toolName.Equals("query_objects", StringComparison.OrdinalIgnoreCase))
        {
            return argumentsJson;
        }

        if (string.IsNullOrWhiteSpace(argumentsJson))
            return argumentsJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return argumentsJson;
        }

        if (root is not JsonObject obj)
            return argumentsJson;

        Rename(obj, "object_type", "objectType");
        Rename(obj, "ObjectType", "objectType");
        Rename(obj, "page_size", "pageSize");
        Rename(obj, "limit", "pageSize");
        Rename(obj, "fields_to_return", "fields");
        Rename(obj, "FieldsToReturn", "fields");
        Rename(obj, "filters", "filter");
        Rename(obj, "Filters", "filter");

        if (obj["objectType"] is JsonValue ot && ot.TryGetValue<string>(out var objectType)
            && !string.IsNullOrWhiteSpace(objectType))
        {
            obj["objectType"] = objectType.Trim().ToLowerInvariant();
        }

        if (obj["filter"] is JsonArray filterArr)
        {
            var normalized = new JsonArray();
            foreach (var item in filterArr)
            {
                var clause = NormalizeFilterItem(item);
                if (!string.IsNullOrWhiteSpace(clause))
                    normalized.Add(clause);
            }

            if (normalized.Count > 0)
                obj["filter"] = normalized;
        }

        if (obj["pageSize"] is JsonValue ps && ps.TryGetValue(out int pageSize))
        {
            if (pageSize < 1) obj["pageSize"] = 1;
            if (pageSize > 99) obj["pageSize"] = 99;
        }

        return obj.ToJsonString();
    }

    private static void Rename(JsonObject obj, string from, string to)
    {
        if (obj.ContainsKey(to) || !obj.TryGetPropertyValue(from, out var value) || value is null)
            return;
        obj[to] = value.DeepClone();
        obj.Remove(from);
    }

    private static string? NormalizeFilterItem(JsonNode? item)
    {
        if (item is null)
            return null;

        if (item is JsonValue scalar && scalar.TryGetValue<string>(out var already)
            && !string.IsNullOrWhiteSpace(already))
        {
            return already.Trim();
        }

        if (item is not JsonObject fo)
            return item.ToJsonString();

        var field = GetString(fo, "field_name")
            ?? GetString(fo, "fieldName")
            ?? GetString(fo, "field")
            ?? GetString(fo, "name");
        var op = GetString(fo, "operator")
            ?? GetString(fo, "op")
            ?? "EQ";
        var value = GetString(fo, "value")
            ?? GetString(fo, "val");

        if (string.IsNullOrWhiteSpace(field) || value is null)
            return null;

        return $"{field.Trim()}.{MapOperator(op)}:{value.Trim()}";
    }

    private static string MapOperator(string op)
    {
        var t = op.Trim();
        return t switch
        {
            "=" or "eq" or "EQ" => "EQ",
            "!=" or "<>" or "ne" or "NE" => "NE",
            ">" or "gt" or "GT" => "GT",
            ">=" or "ge" or "GE" => "GE",
            "<" or "lt" or "LT" => "LT",
            "<=" or "le" or "LE" => "LE",
            "sw" or "SW" or "starts_with" or "startswith" => "SW",
            "in" or "IN" => "IN",
            _ => t.ToUpperInvariant()
        };
    }

    private static string? GetString(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
            return s;
        return node.ToJsonString().Trim('"');
    }
}
