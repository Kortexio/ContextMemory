using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Agentic.Mcp;

/// <summary>
/// MCP call shaping from Admin <see cref="AgenticToolsConfig"/>:
/// query lexicon (selection) + argument aliases (execution). Empty maps ⇒ no-op.
/// </summary>
public static class McpArgumentShaping
{
    public static string ExpandQuery(string? userQuery, IReadOnlyDictionary<string, string>? lexicon)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return userQuery ?? string.Empty;

        if (lexicon is null || lexicon.Count == 0)
            return userQuery.Trim();

        var original = userQuery.Trim();
        var lower = RemoveDiacritics(original).ToLowerInvariant();
        var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (source, english) in lexicon.OrderByDescending(m => m.Key.Length))
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(english))
                continue;

            var srcNorm = RemoveDiacritics(source).ToLowerInvariant();
            if (!lower.Contains(srcNorm, StringComparison.Ordinal))
                continue;

            foreach (var token in english.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                extras.Add(token);
        }

        return extras.Count == 0
            ? original
            : original + " " + string.Join(' ', extras);
    }

    public static string NormalizeArguments(
        string toolName,
        string argumentsJson,
        AgenticToolsConfig? toolsConfig = null)
    {
        var normalizer = ResolveNormalizer(toolName, toolsConfig);
        if (normalizer is null || string.IsNullOrWhiteSpace(argumentsJson))
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

        foreach (var (from, to) in normalizer.Aliases)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;
            Rename(obj, from, to);
        }

        foreach (var drop in normalizer.DropKeys)
        {
            if (string.IsNullOrWhiteSpace(drop))
                continue;
            RemoveIgnoreCase(obj, drop);
        }

        if (normalizer.LowercaseObjectType
            && obj["objectType"] is JsonValue ot
            && ot.TryGetValue<string>(out var objectType)
            && !string.IsNullOrWhiteSpace(objectType))
        {
            obj["objectType"] = objectType.Trim().ToLowerInvariant();
        }

        NormalizeFilterProperty(obj);

        var maxPage = normalizer.MaxPageSize > 0 ? normalizer.MaxPageSize : 99;
        if (obj["pageSize"] is JsonValue ps && ps.TryGetValue(out int pageSize))
        {
            if (pageSize < 1) obj["pageSize"] = 1;
            if (pageSize > maxPage) obj["pageSize"] = maxPage;
        }

        return obj.ToJsonString();
    }

    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Platform fallback when Admin left <c>argumentNormalizers</c> empty.
    /// Admin entries always win.
    /// </summary>
    private static readonly McpArgumentNormalizerConfig DefaultQueryObjectsNormalizer = new()
    {
        Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["object_type"] = "objectType",
            ["page_size"] = "pageSize",
            ["limit"] = "pageSize",
            ["fields_to_return"] = "fields",
            ["fieldsToReturn"] = "fields",
            ["filters"] = "filter"
        },
        DropKeys = ["fieldsToReturn", "fields_to_return", "object_type", "limit", "filters"],
        LowercaseObjectType = true,
        MaxPageSize = 99
    };

    private static McpArgumentNormalizerConfig? ResolveNormalizer(
        string toolName,
        AgenticToolsConfig? toolsConfig)
    {
        var map = toolsConfig?.ArgumentNormalizers;
        if (map is not null && map.Count > 0)
        {
            if (map.TryGetValue(toolName, out var exact))
                return exact;

            foreach (var (suffix, cfg) in map)
            {
                if (string.IsNullOrWhiteSpace(suffix))
                    continue;
                if (toolName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    || toolName.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    return cfg;
            }
        }

        if (toolName.EndsWith("query_objects", StringComparison.OrdinalIgnoreCase)
            || toolName.Equals("query_objects", StringComparison.OrdinalIgnoreCase))
            return DefaultQueryObjectsNormalizer;

        return null;
    }

    private static void Rename(JsonObject obj, string from, string to)
    {
        if (FindPropertyKey(obj, to) is not null)
            return;

        var actualFrom = FindPropertyKey(obj, from);
        if (actualFrom is null || !obj.TryGetPropertyValue(actualFrom, out var value) || value is null)
            return;

        obj[to] = value.DeepClone();
        obj.Remove(actualFrom);
    }

    private static void RemoveIgnoreCase(JsonObject obj, string name)
    {
        var key = FindPropertyKey(obj, name);
        if (key is not null)
            obj.Remove(key);
    }

    private static string? FindPropertyKey(JsonObject obj, string name)
    {
        foreach (var prop in obj)
        {
            if (string.Equals(prop.Key, name, StringComparison.OrdinalIgnoreCase))
                return prop.Key;
        }

        return null;
    }

    private static void NormalizeFilterProperty(JsonObject obj)
    {
        if (obj["filter"] is JsonObject filterObj)
        {
            var clause = NormalizeFilterItem(filterObj);
            if (!string.IsNullOrWhiteSpace(clause))
                obj["filter"] = new JsonArray(clause!);
        }
        else if (obj["filter"] is JsonValue filterScalar
                 && filterScalar.TryGetValue<string>(out var filterText)
                 && !string.IsNullOrWhiteSpace(filterText))
        {
            obj["filter"] = new JsonArray(NormalizeFilterClause(filterText.Trim()));
        }
        else if (obj["filter"] is JsonArray filterArr)
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
    }

    private static string? NormalizeFilterItem(JsonNode? item)
    {
        if (item is null)
            return null;

        if (item is JsonValue scalar && scalar.TryGetValue<string>(out var already)
            && !string.IsNullOrWhiteSpace(already))
            return NormalizeFilterClause(already.Trim());

        if (item is not JsonObject fo)
            return item.ToJsonString();

        var field = GetString(fo, "field_name")
            ?? GetString(fo, "fieldName")
            ?? GetString(fo, "field")
            ?? GetString(fo, "name");
        var op = GetString(fo, "operator") ?? GetString(fo, "op") ?? "EQ";
        var value = GetString(fo, "value") ?? GetString(fo, "val");

        if (string.IsNullOrWhiteSpace(field) || value is null)
            return null;

        return $"{field.Trim()}.{MapOperator(op)}:{value.Trim()}";
    }

    private static string NormalizeFilterClause(string clause)
    {
        if (clause.Contains('.', StringComparison.Ordinal) && clause.Contains(':', StringComparison.Ordinal))
            return clause;

        var m = System.Text.RegularExpressions.Regex.Match(
            clause,
            """^\s*(?<field>[A-Za-z_][\w]*)\s*(?<op>=|==|!=|<>|>=|<=|>|<|EQ|NE|GT|GE|LT|LE|SW|IN)\s*['"]?(?<value>[^'"]+?)['"]?\s*$""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        if (!m.Success)
            return clause;

        return $"{m.Groups["field"].Value}.{MapOperator(m.Groups["op"].Value)}:{m.Groups["value"].Value.Trim()}";
    }

    private static string MapOperator(string op) =>
        op.Trim() switch
        {
            "=" or "eq" or "EQ" => "EQ",
            "!=" or "<>" or "ne" or "NE" => "NE",
            ">" or "gt" or "GT" => "GT",
            ">=" or "ge" or "GE" => "GE",
            "<" or "lt" or "LT" => "LT",
            "<=" or "le" or "LE" => "LE",
            "sw" or "SW" or "starts_with" or "startswith" => "SW",
            "in" or "IN" => "IN",
            var t => t.ToUpperInvariant()
        };

    private static string? GetString(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
            return s;
        return node.ToJsonString().Trim('"');
    }
}
