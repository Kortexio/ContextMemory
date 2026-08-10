using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// JSON format + optional lightweight schema checks (json-format / openapi-response kinds).
/// </summary>
public static partial class AgenticJsonSchemaGuardrail
{
    public static bool TryGetRejectionFeedback(
        string kind,
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var requireJson = string.Equals(kind, AgenticGuardrailKinds.JsonFormat, StringComparison.OrdinalIgnoreCase);
        var schemaJson = AgenticGuardrailConfigReader.GetJsonSchema(configJson);

        if (string.Equals(kind, AgenticGuardrailKinds.OpenApiResponse, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(schemaJson))
        {
            return false; // no-op without schema
        }

        if (!TryExtractJson(finalAnswer, out var jsonText))
        {
            if (requireJson || !string.IsNullOrWhiteSpace(schemaJson))
            {
                feedback = Feedback(configJson, runtimeConfig);
                return true;
            }

            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException)
        {
            feedback = Feedback(configJson, runtimeConfig);
            return true;
        }

        using (doc)
        {
            if (!string.IsNullOrWhiteSpace(schemaJson)
                && !SatisfiesLightweightSchema(doc.RootElement, schemaJson))
            {
                feedback = Feedback(configJson, runtimeConfig);
                return true;
            }
        }

        return false;
    }

    private static string Feedback(string configJson, AppRuntimeConfig runtimeConfig) =>
        AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
        ?? TenantLocale.Select(
            runtimeConfig.DefaultLanguage,
            "Rejected: answer does not satisfy JSON/schema requirements.",
            "Rejeitado: a resposta não cumpre requisitos de JSON/schema.");

    private static bool TryExtractJson(string text, out string json)
    {
        var trimmed = text.Trim();
        var fence = JsonFenceRegex().Match(trimmed);
        if (fence.Success)
        {
            json = fence.Groups[1].Value.Trim();
            return true;
        }

        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            json = trimmed;
            return true;
        }

        json = string.Empty;
        return false;
    }

    /// <summary>
    /// Minimal subset: type=object/array/string/number/boolean, required[], properties{name:{type}}.
    /// </summary>
    private static bool SatisfiesLightweightSchema(JsonElement value, string schemaJson)
    {
        try
        {
            using var schemaDoc = JsonDocument.Parse(schemaJson);
            return MatchSchema(value, schemaDoc.RootElement);
        }
        catch
        {
            return true; // malformed schema → do not block
        }
    }

    private static bool MatchSchema(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var typeEl)
            && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString() ?? "";
            var ok = type switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind is JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => true
            };
            if (!ok)
                return false;
        }

        if (value.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var req in required.EnumerateArray())
            {
                if (req.ValueKind != JsonValueKind.String)
                    continue;
                var name = req.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!value.TryGetProperty(name, out _))
                    return false;
            }
        }

        if (value.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                if (!value.TryGetProperty(prop.Name, out var child))
                    continue;
                if (!MatchSchema(child, prop.Value))
                    return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFenceRegex();
}
