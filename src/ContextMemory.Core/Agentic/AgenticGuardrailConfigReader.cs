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

    public static IReadOnlyList<string> GetBlockedPatterns(string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("patterns", out var patterns)
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
}
