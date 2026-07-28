using System.Text.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class OllamaFunctionArgumentsConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Deserializes_Arguments_As_Json_Object()
    {
        const string json =
            """
            {
              "model": "llama3.2",
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  {
                    "function": {
                      "name": "shell_execute",
                      "arguments": {"command":"echo hello"}
                    }
                  }
                ]
              },
              "done": true
            }
            """;

        var response = JsonSerializer.Deserialize<OllamaResponse>(json, JsonOptions);

        Assert.NotNull(response);
        var call = Assert.Single(response!.Message!.ToolCalls!);
        Assert.Equal("shell_execute", call.Function.Name);
        Assert.Equal("""{"command":"echo hello"}""", call.Function.Arguments);
    }

    [Fact]
    public void Deserializes_Arguments_As_Json_String()
    {
        const string json =
            """
            {
              "model": "llama3.2",
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  {
                    "function": {
                      "name": "shell_execute",
                      "arguments": "{\"command\":\"echo hello\"}"
                    }
                  }
                ]
              },
              "done": true
            }
            """;

        var response = JsonSerializer.Deserialize<OllamaResponse>(json, JsonOptions);

        Assert.NotNull(response);
        var call = Assert.Single(response!.Message!.ToolCalls!);
        Assert.Equal("shell_execute", call.Function.Name);
        Assert.Equal("""{"command":"echo hello"}""", call.Function.Arguments);
    }

    [Fact]
    public void Serializes_Arguments_As_Json_Object()
    {
        var call = new OllamaFunctionCall("shell_execute", """{"command":"echo hello"}""");
        var json = JsonSerializer.Serialize(call, JsonOptions);

        Assert.Contains("\"arguments\":{\"command\":\"echo hello\"}", json);
        Assert.DoesNotContain("\"arguments\":\"{", json);
    }

    [Fact]
    public void RoundTrips_Object_Arguments_For_Ollama_Replay()
    {
        const string inbound =
            """
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "function": {
                    "name": "wiki_search",
                    "arguments": { "query": "billing", "topK": 3 }
                  }
                }
              ]
            }
            """;

        var message = JsonSerializer.Deserialize<OllamaMessage>(inbound, JsonOptions);
        Assert.NotNull(message);

        var outbound = JsonSerializer.Serialize(message, JsonOptions);
        using var doc = JsonDocument.Parse(outbound);
        var args = doc.RootElement
            .GetProperty("tool_calls")[0]
            .GetProperty("function")
            .GetProperty("arguments");

        Assert.Equal(JsonValueKind.Object, args.ValueKind);
        Assert.Equal("billing", args.GetProperty("query").GetString());
        Assert.Equal(3, args.GetProperty("topK").GetInt32());
    }
}
