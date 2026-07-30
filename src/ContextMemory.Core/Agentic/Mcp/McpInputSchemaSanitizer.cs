using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// Simplifies MCP JSON Schemas for llama.cpp / Ollama tool-calling grammars.
/// Complex schemas (e.g. additionalProperties:false + large nested objects) often cause
/// "Failed to initialize samplers: failed to parse grammar".
/// </summary>
public static class McpInputSchemaSanitizer
{
    private static readonly HashSet<string> DroppedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "$schema",
        "$id",
        "$ref",
        "$defs",
        "definitions",
        "oneOf",
        "anyOf",
        "allOf",
        "not",
        "if",
        "then",
        "else",
        "dependentSchemas",
        "dependentRequired",
        "patternProperties",
        "unevaluatedProperties",
        "unevaluatedItems",
        "contentMediaType",
        "contentEncoding",
        "examples",
        "default",
        "const",
        "title",
        "deprecated",
        "readOnly",
        "writeOnly"
    };

    private const int MaxDepth = 3;
    private const int MaxProperties = 24;
    private const int MaxDescriptionChars = 240;
    private const int MaxSerializedChars = 6_000;

    public static object Sanitize(object? schema)
    {
        if (schema is null)
            return MinimalObjectSchema();

        JsonNode? node;
        try
        {
            node = schema switch
            {
                JsonNode n => n.DeepClone(),
                JsonElement el => JsonNode.Parse(el.GetRawText()),
                string s => JsonNode.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s),
                _ => JsonNode.Parse(JsonSerializer.Serialize(schema))
            };
        }
        catch
        {
            return MinimalObjectSchema();
        }

        if (node is null)
            return MinimalObjectSchema();

        var cleaned = Clean(node, depth: 0) ?? MinimalObjectSchemaNode();
        EnsureObjectRoot(cleaned);

        var json = cleaned.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        if (json.Length > MaxSerializedChars)
            cleaned = CollapseToTopLevel(cleaned);

        return JsonSerializer.Deserialize<object>(cleaned.ToJsonString()) ?? MinimalObjectSchema();
    }

    private static JsonNode? Clean(JsonNode? node, int depth)
    {
        if (node is null)
            return null;

        if (node is JsonValue)
            return node.DeepClone();

        if (node is JsonArray arr)
        {
            var next = new JsonArray();
            foreach (var item in arr)
            {
                var cleaned = Clean(item, depth);
                if (cleaned is not null)
                    next.Add(cleaned);
            }
            return next;
        }

        if (node is not JsonObject obj)
            return null;

        if (depth > MaxDepth)
            return SimplifyLeaf(obj);

        var result = new JsonObject();

        // Normalize type arrays like ["string","null"] -> "string"
        if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonArray typeArr)
        {
            var primary = typeArr
                .Select(x => x?.GetValue<string>())
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "null", StringComparison.OrdinalIgnoreCase));
            result["type"] = primary ?? "object";
        }
        else if (obj.TryGetPropertyValue("type", out var typeScalar) && typeScalar is not null)
        {
            result["type"] = typeScalar.DeepClone();
        }

        if (obj.TryGetPropertyValue("description", out var desc) && desc is JsonValue descVal)
        {
            var text = descVal.GetValue<string>() ?? string.Empty;
            if (text.Length > MaxDescriptionChars)
                text = text[..MaxDescriptionChars].TrimEnd() + "…";
            if (!string.IsNullOrWhiteSpace(text))
                result["description"] = text;
        }

        if (obj.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr)
        {
            var enums = new JsonArray();
            foreach (var item in enumArr.Take(32))
            {
                if (item is JsonValue)
                    enums.Add(item.DeepClone());
            }
            if (enums.Count > 0)
                result["enum"] = enums;
        }

        if (obj.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray requiredArr)
        {
            var required = new JsonArray();
            foreach (var item in requiredArr)
            {
                if (item is JsonValue v)
                {
                    var name = v.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name))
                        required.Add(name);
                }
            }
            if (required.Count > 0)
                result["required"] = required;
        }

        // Drop additionalProperties:false — common llama.cpp grammar breaker with large schemas.
        // Keep true if present; ignore object-valued additionalProperties.
        if (obj.TryGetPropertyValue("additionalProperties", out var addProps))
        {
            if (addProps is JsonValue addVal && addVal.TryGetValue<bool>(out var flag) && flag)
                result["additionalProperties"] = true;
        }

        if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props)
        {
            var requiredNames = result["required"] is JsonArray req
                ? req.Select(x => x?.GetValue<string>() ?? string.Empty)
                    .Where(x => x.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ordered = props
                .OrderByDescending(p => requiredNames.Contains(p.Key))
                .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxProperties);

            var cleanedProps = new JsonObject();
            foreach (var (key, value) in ordered)
            {
                var cleaned = Clean(value, depth + 1) ?? new JsonObject { ["type"] = "string" };
                cleanedProps[key] = cleaned;
            }

            result["properties"] = cleanedProps;

            // Keep required only for properties we retained.
            if (result["required"] is JsonArray reqArr)
            {
                var filtered = new JsonArray();
                foreach (var item in reqArr)
                {
                    var name = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name) && cleanedProps.ContainsKey(name))
                        filtered.Add(name);
                }
                if (filtered.Count > 0)
                    result["required"] = filtered;
                else
                    result.Remove("required");
            }
        }

        if (obj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is not null)
        {
            result["items"] = Clean(itemsNode, depth + 1) ?? new JsonObject { ["type"] = "string" };
        }

        // Copy simple numeric constraints that grammars usually accept.
        foreach (var key in new[] { "minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems" })
        {
            if (obj.TryGetPropertyValue(key, out var constraint) && constraint is JsonValue)
                result[key] = constraint.DeepClone();
        }

        // Drop everything else that often breaks grammar conversion.
        foreach (var key in DroppedKeywords)
            result.Remove(key);

        if (!result.ContainsKey("type") && result.ContainsKey("properties"))
            result["type"] = "object";

        if (!result.ContainsKey("type") && result.ContainsKey("items"))
            result["type"] = "array";

        if (!result.ContainsKey("type"))
            result["type"] = "string";

        return result;
    }

    private static JsonObject SimplifyLeaf(JsonObject obj)
    {
        var type = "string";
        if (obj.TryGetPropertyValue("type", out var typeNode))
        {
            if (typeNode is JsonValue v)
                type = v.GetValue<string>() ?? "string";
            else if (typeNode is JsonArray arr)
            {
                type = arr.Select(x => x?.GetValue<string>())
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "null", StringComparison.OrdinalIgnoreCase))
                    ?? "string";
            }
        }

        if (string.Equals(type, "object", StringComparison.OrdinalIgnoreCase))
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = true };

        if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase))
            return new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };

        return new JsonObject { ["type"] = type };
    }

    private static JsonObject CollapseToTopLevel(JsonNode node)
    {
        if (node is not JsonObject obj)
            return MinimalObjectSchemaNode();

        var collapsed = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true
        };

        if (obj.TryGetPropertyValue("required", out var required) && required is JsonArray reqArr)
            collapsed["required"] = reqArr.DeepClone();

        var props = new JsonObject();
        if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
        {
            foreach (var (key, value) in propsObj.Take(MaxProperties))
            {
                var type = "string";
                if (value is JsonObject valueObj && valueObj.TryGetPropertyValue("type", out var t) && t is JsonValue tv)
                    type = tv.GetValue<string>() ?? "string";
                if (string.Equals(type, "object", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "array", StringComparison.OrdinalIgnoreCase))
                {
                    props[key] = string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)
                        ? new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
                        : new JsonObject { ["type"] = "object", ["additionalProperties"] = true };
                }
                else
                {
                    props[key] = new JsonObject { ["type"] = type };
                }
            }
        }

        collapsed["properties"] = props;
        return collapsed;
    }

    private static void EnsureObjectRoot(JsonNode node)
    {
        if (node is JsonObject obj && !obj.ContainsKey("type"))
            obj["type"] = "object";
    }

    private static object MinimalObjectSchema() =>
        JsonSerializer.Deserialize<object>(MinimalObjectSchemaNode().ToJsonString())!;

    private static JsonObject MinimalObjectSchemaNode() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["additionalProperties"] = true
        };
}
