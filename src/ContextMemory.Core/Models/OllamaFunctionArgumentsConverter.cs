using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

/// <summary>
/// Ollama may return tool-call <c>arguments</c> as a JSON object or as a JSON string.
/// Internally we keep a JSON string so executors can parse uniformly; when writing
/// back to Ollama we emit a JSON object (string form breaks multi-turn tool calling).
/// </summary>
public sealed class OllamaFunctionArgumentsConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "{}",
            JsonTokenType.Null => "{}",
            JsonTokenType.StartObject
                or JsonTokenType.StartArray
                or JsonTokenType.True
                or JsonTokenType.False
                or JsonTokenType.Number =>
                JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            _ => throw new JsonException(
                $"Unexpected token type '{reader.TokenType}' for Ollama function arguments.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            doc.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            // Non-JSON argument payloads: wrap as a single string property so Ollama
            // still receives an object (native /api/chat rejects string arguments).
            writer.WriteStartObject();
            writer.WriteString("value", value);
            writer.WriteEndObject();
        }
    }
}
