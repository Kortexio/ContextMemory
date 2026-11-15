using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic.Mcp;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Ajv-style arg repair against a tool's JSON Schema (CodeForge schemaRepair rules, subset).
/// </summary>
public static class ToolArgumentRepair
{
    public sealed record Result(
        string ToolName,
        string ArgumentsJson,
        bool Changed,
        bool NameRecovered,
        IReadOnlyList<string> AppliedRules);

    public static Result Repair(
        string toolName,
        string? argumentsJson,
        object? schema,
        IReadOnlySet<string>? activeToolNames)
    {
        var rules = new List<string>();
        var name = toolName?.Trim() ?? string.Empty;
        var nameRecovered = false;

        if (activeToolNames is { Count: > 0 } && !activeToolNames.Contains(name))
        {
            var phantom = McpPhantomToolResolver.Resolve(name, activeToolNames);
            if (phantom is not null)
            {
                name = phantom;
                nameRecovered = true;
                rules.Add("remapPhantomMcpName");
            }
            else
            {
                var recovered = RecoverMangledName(name, activeToolNames);
                if (recovered is not null)
                {
                    name = recovered;
                    nameRecovered = true;
                    rules.Add("recoverMangledToolName");
                }
            }
        }

        JsonObject root;
        try
        {
            var node = string.IsNullOrWhiteSpace(argumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(argumentsJson);
            if (node is JsonValue jv && jv.TryGetValue<string>(out var s) && s.TrimStart().StartsWith('{'))
            {
                node = JsonNode.Parse(s);
                rules.Add("parseJsonStringifiedObject");
            }

            root = node as JsonObject ?? WrapRoot(node, rules);
        }
        catch
        {
            return new Result(name, argumentsJson ?? "{}", Changed: false, nameRecovered, rules);
        }

        DropNullOrUndefined(root, rules);
        DropEmptyObjectPlaceholders(root, rules);
        CoerceFromSchema(root, schema, rules);

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var changed = nameRecovered
            || !string.Equals(json, NormalizeJson(argumentsJson), StringComparison.Ordinal);
        return new Result(name, json, changed, nameRecovered, rules);
    }

    private static string NormalizeJson(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return "{}";
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.GetRawText();
        }
        catch
        {
            return argumentsJson.Trim();
        }
    }

    private static JsonObject WrapRoot(JsonNode? node, List<string> rules)
    {
        if (node is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            rules.Add("wrapRootStringAsObject");
            return new JsonObject { ["value"] = s };
        }

        rules.Add("wrapRootAsObject");
        return new JsonObject { ["value"] = node?.DeepClone() };
    }

    private static void DropNullOrUndefined(JsonObject obj, List<string> rules)
    {
        foreach (var key in obj.Select(p => p.Key).ToList())
        {
            if (obj[key] is null)
            {
                obj.Remove(key);
                rules.Add("dropNullOrUndefined");
            }
            else if (obj[key] is JsonObject child)
            {
                DropNullOrUndefined(child, rules);
            }
        }
    }

    private static void DropEmptyObjectPlaceholders(JsonObject obj, List<string> rules)
    {
        foreach (var key in obj.Select(p => p.Key).ToList())
        {
            if (obj[key] is JsonObject child && child.Count == 0
                && key is "parameters" or "arguments" or "args" or "input")
            {
                obj.Remove(key);
                rules.Add("dropEmptyObjectPlaceholder");
            }
        }
    }

    private static void CoerceFromSchema(JsonObject root, object? schema, List<string> rules)
    {
        JsonObject? props = null;
        JsonArray? required = null;
        try
        {
            var schemaNode = schema switch
            {
                JsonNode jn => jn,
                JsonElement el => JsonNode.Parse(el.GetRawText()),
                string s => JsonNode.Parse(s),
                null => null,
                _ => JsonNode.Parse(JsonSerializer.Serialize(schema))
            };
            props = schemaNode?["properties"] as JsonObject;
            required = schemaNode?["required"] as JsonArray;
        }
        catch
        {
            return;
        }

        if (props is null)
            return;

        foreach (var prop in props)
        {
            var key = prop.Key;
            if (!root.ContainsKey(key) || root[key] is null)
                continue;

            var type = prop.Value?["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type))
                continue;

            if (root[key] is JsonValue jv && jv.TryGetValue<string>(out var asString))
            {
                if (type is "integer" or "number"
                    && double.TryParse(asString, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    root[key] = type == "integer" ? (int)num : num;
                    rules.Add("coerceNumericString");
                }
                else if (type == "boolean"
                         && bool.TryParse(asString, out var b))
                {
                    root[key] = b;
                    rules.Add("coerceBooleanString");
                }
                else if (type == "array" && asString.TrimStart().StartsWith('['))
                {
                    try
                    {
                        root[key] = JsonNode.Parse(asString);
                        rules.Add("parseJsonStringifiedArray");
                    }
                    catch { /* keep */ }
                }
                else if (type == "object" && asString.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        root[key] = JsonNode.Parse(asString);
                        rules.Add("parseJsonStringifiedObject");
                    }
                    catch { /* keep */ }
                }
            }
        }

        // If required keys missing but a single string "value" exists and one required string prop — map it.
        if (required is { Count: 1 }
            && required[0]?.GetValue<string>() is { } reqKey
            && !root.ContainsKey(reqKey)
            && root["value"] is JsonValue only
            && only.TryGetValue<string>(out var mapped))
        {
            root[reqKey] = mapped;
            root.Remove("value");
            rules.Add("mapValueToRequired");
        }
    }

    private static string? RecoverMangledName(string name, IReadOnlySet<string> active)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var exact = active.FirstOrDefault(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // Fuzzy: contains or levenshtein-ish simple
        var candidates = active
            .Select(a => (Name: a, Dist: Levenshtein(name.ToLowerInvariant(), a.ToLowerInvariant())))
            .Where(x => x.Dist <= Math.Max(2, name.Length / 4))
            .OrderBy(x => x.Dist)
            .ToList();
        return candidates.Count > 0 ? candidates[0].Name : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
