using System.Text.Json;

using ContextMemory.Core.Models;

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

            // Harness feedback is English-only; the model translates user-facing answers.
            // Prefer "feedback", then legacy "feedbackEn". Ignore language / feedbackPt.
            _ = language;
            if (TryReadString(root, "feedback", out var feedback))
                return feedback;
            if (TryReadString(root, "feedbackEn", out var feedbackEn))
                return feedbackEn;
        }
        catch
        {
            // ignore malformed config
        }

        return null;
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var el)
            || el.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(el.GetString()))
        {
            return false;
        }

        value = el.GetString();
        return true;
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

    public static string ResolveFeedback(
        string configJson,
        string? language,
        string? matchedPattern = null)
    {
        var fromAdmin = GetFeedback(configJson, language);
        if (!string.IsNullOrWhiteSpace(fromAdmin))
            return fromAdmin;

        if (!string.IsNullOrWhiteSpace(matchedPattern))
            return $"Rejected: blocked pattern '{matchedPattern}'.";

        return "Rejected by guardrail policy.";
    }

    public static string ForKind(ResolvedAgenticPolicy policy, string kind) =>
        policy.FindByKind(kind)?.ConfigJson ?? "{}";

    public static bool TryGetKind(
        ResolvedAgenticPolicy policy,
        string kind,
        out string configJson)
    {
        if (!policy.HasKind(kind))
        {
            configJson = "{}";
            return false;
        }

        configJson = ForKind(policy, kind);
        return true;
    }

    public static bool TryMatchPatterns(
        string text,
        string configJson,
        string? language,
        out string feedback,
        string patternsKey = "patterns")
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var patterns = GetStringList(configJson, patternsKey);
        if (patterns.Count == 0)
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            if (!text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            feedback = ResolveFeedback(configJson, language, pattern);
            return true;
        }

        return false;
    }
}
