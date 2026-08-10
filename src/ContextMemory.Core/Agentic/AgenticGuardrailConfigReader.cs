using System.Text.Json;

namespace ContextMemory.Core.Agentic;

public static class AgenticGuardrailConfigReader
{
    public static string? GetFeedback(string configJson, string? language)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            var preferPt = language is not null
                           && language.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

            if (preferPt
                && root.TryGetProperty("feedbackPt", out var pt)
                && pt.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(pt.GetString()))
            {
                return pt.GetString();
            }

            if (root.TryGetProperty("feedbackEn", out var en)
                && en.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(en.GetString()))
            {
                return en.GetString();
            }
        }
        catch
        {
            // ignore malformed config
        }

        return null;
    }

    public static IReadOnlyList<string> GetBlockedPatterns(string configJson) =>
        GetStringList(configJson, "patterns");

    public static IReadOnlyList<string> GetStringList(string configJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(configJson) || string.IsNullOrWhiteSpace(propertyName))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var patterns)
                || patterns.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<string>();
            foreach (var item in patterns.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s);
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public static string? GetString(string configJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(configJson) || string.IsNullOrWhiteSpace(propertyName))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static int GetInt(string configJson, string propertyName, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configJson) || string.IsNullOrWhiteSpace(propertyName))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var el)
                && el.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch
        {
            // ignore
        }

        return defaultValue;
    }

    public static double GetDouble(string configJson, string propertyName, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configJson) || string.IsNullOrWhiteSpace(propertyName))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var el)
                && el.TryGetDouble(out var value))
            {
                return value;
            }
        }
        catch
        {
            // ignore
        }

        return defaultValue;
    }

    /// <summary>Returns raw JSON text of a nested <c>schema</c> object/array, or null.</summary>
    public static string? GetJsonSchema(string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("schema", out var schema))
                return null;
            if (schema.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return schema.GetRawText();
            if (schema.ValueKind == JsonValueKind.String)
            {
                var s = schema.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
