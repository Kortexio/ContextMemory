using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

/// <summary>
/// Ollama may return tool-call <c>arguments</c> as a JSON object or as a JSON string.
/// Internally we always keep a JSON string so executors can parse uniformly.
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
        => writer.WriteStringValue(value ?? "{}");
}
