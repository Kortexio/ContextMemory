using System.Text.Json;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Reads LLM-emitted tool arguments while tolerating common naming variations.
/// Schemas use camelCase, but weak/OpenAI-compatible models often emit snake_case.
/// </summary>
public static class AgenticToolArguments
{
    public static string? GetString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    public static int GetInt(JsonElement root, string name, int fallback) =>
        TryGetProperty(root, name, out var element)
        && element.TryGetInt32(out var value)
        && value > 0
            ? value
            : fallback;

    public static bool? GetBool(JsonElement root, string name) =>
        TryGetProperty(root, name, out var element)
        && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;

    public static bool TryGetProperty(
        JsonElement root,
        string expectedName,
        out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (root.TryGetProperty(expectedName, out value))
            return true;

        var normalizedExpected = NormalizeName(expectedName);
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(
                    NormalizeName(property.Name),
                    normalizedExpected,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeName(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
}
