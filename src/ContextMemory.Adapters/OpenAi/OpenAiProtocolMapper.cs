using System.Text.Json;
using ContextMemory.Adapters.OpenAi;
using ContextMemory.Core.Models;

namespace ContextMemory.Adapters.OpenAi;

/// <summary>Maps OpenAI-compatible public wire format to/from internal Ollama models.</summary>
public static class OpenAiProtocolMapper
{
    public static OllamaRequest ToOllamaRequest(OpenAiCompatibleChatRequest request)
    {
        var messages = request.Messages.Select(ToOllamaMessage).ToList();
        List<OllamaTool>? tools = null;
        if (request.Tools is { Count: > 0 })
        {
            tools = request.Tools.Select(t => new OllamaTool(
                string.IsNullOrWhiteSpace(t.Type) ? "function" : t.Type,
                new OllamaFunction(t.Function.Name, t.Function.Description, t.Function.Parameters))).ToList();
        }

        OllamaOptions? options = null;
        if (request.Temperature is not null
            || request.TopP is not null
            || request.MaxTokens is not null
            || request.Seed is not null
            || request.Stop is not null)
        {
            options = new OllamaOptions
            {
                Temperature = request.Temperature,
                TopP = request.TopP,
                NumPredict = request.MaxTokens,
                Seed = request.Seed,
                Stop = ParseStop(request.Stop)
            };
        }

        return new OllamaRequest
        {
            Model = request.Model,
            Messages = messages,
            Stream = request.Stream,
            Tools = tools,
            Options = options,
            Think = false
        };
    }

    public static OpenAiCompatibleChatResponse ToChatResponse(string model, OllamaResponse response, string? completionId = null)
    {
        var content = response.Message?.Content ?? response.Response ?? string.Empty;
        var toolCalls = ToOpenAiToolCalls(response.Message?.ToolCalls);
        var finish = string.Equals(response.DoneReason, "tool_calls", StringComparison.OrdinalIgnoreCase)
            || toolCalls is { Count: > 0 }
            ? "tool_calls"
            : "stop";

        return new OpenAiCompatibleChatResponse
        {
            Id = completionId ?? $"chatcmpl-{Guid.NewGuid():N}",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = string.IsNullOrWhiteSpace(response.Model) ? model : response.Model,
            Choices =
            [
                new OpenAiCompatibleChoice
                {
                    Index = 0,
                    Message = new OpenAiCompatibleChatMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.SerializeToElement(content),
                        ToolCalls = toolCalls
                    },
                    FinishReason = response.Done ? finish : null
                }
            ],
            ContextMemory = response.ContextMemory
        };
    }

    public static OpenAiCompatibleChatResponse ToStreamChunk(
        string model,
        OllamaResponse chunk,
        string completionId,
        bool isFinal)
    {
        var content = chunk.Message?.Content ?? chunk.Response ?? string.Empty;
        var toolCalls = ToOpenAiToolCalls(chunk.Message?.ToolCalls);

        return new OpenAiCompatibleChatResponse
        {
            Id = completionId,
            Object = "chat.completion.chunk",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = string.IsNullOrWhiteSpace(chunk.Model) ? model : chunk.Model,
            Choices =
            [
                new OpenAiCompatibleChoice
                {
                    Index = 0,
                    Delta = new OpenAiCompatibleChatMessage
                    {
                        Role = isFinal ? null : "assistant",
                        Content = string.IsNullOrEmpty(content)
                            ? null
                            : JsonSerializer.SerializeToElement(content),
                        ToolCalls = toolCalls
                    },
                    FinishReason = isFinal || chunk.Done ? (toolCalls is { Count: > 0 } ? "tool_calls" : "stop") : null
                }
            ],
            ContextMemory = chunk.ContextMemory
        };
    }

    private static OllamaMessage ToOllamaMessage(OpenAiCompatibleChatMessage message)
    {
        List<OllamaToolCall>? toolCalls = null;
        if (message.ToolCalls is { Count: > 0 })
        {
            toolCalls = message.ToolCalls
                .Select(tc => new OllamaToolCall(
                    new OllamaFunctionCall(
                        tc.Function.Name,
                        string.IsNullOrWhiteSpace(tc.Function.Arguments) ? "{}" : tc.Function.Arguments)))
                .ToList();
        }

        return new OllamaMessage
        {
            Role = message.Role ?? "user",
            Content = ExtractTextContent(message.Content),
            ToolCalls = toolCalls
        };
    }

    private static string ExtractTextContent(JsonElement? content)
    {
        if (content is null || content.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return string.Empty;

        var el = content.Value;
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? string.Empty;

        if (el.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var part in el.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                    continue;
                if (part.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && part.TryGetProperty("text", out var text))
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
                else if (part.TryGetProperty("text", out var plain))
                {
                    parts.Add(plain.GetString() ?? string.Empty);
                }
            }

            return string.Join("\n", parts.Where(p => p.Length > 0));
        }

        return el.ToString();
    }

    private static List<OpenAiCompatibleToolCall>? ToOpenAiToolCalls(List<OllamaToolCall>? toolCalls)
    {
        if (toolCalls is null || toolCalls.Count == 0)
            return null;

        return toolCalls
            .Select((tc, i) => new OpenAiCompatibleToolCall
            {
                Id = $"call_{i}",
                Type = "function",
                Function = new OpenAiCompatibleFunctionCall
                {
                    Name = tc.Function.Name,
                    Arguments = string.IsNullOrWhiteSpace(tc.Function.Arguments) ? "{}" : tc.Function.Arguments
                }
            })
            .ToList();
    }

    private static List<string>? ParseStop(JsonElement? stop)
    {
        if (stop is null || stop.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;

        var el = stop.Value;
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return string.IsNullOrEmpty(s) ? null : [s];
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => s.Length > 0)
                .ToList();
        }

        return null;
    }
}
