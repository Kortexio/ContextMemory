using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextMemory.Adapters.OpenAi;

internal sealed class OpenAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly ILogger _logger;

    public OpenAiChatClient(HttpClient httpClient, string baseUrl, string? apiKey, ILogger? logger = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken)
    {
        var payload = MapRequest(request, stream: false);
        using var httpRequest = CreateRequest(payload);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var preview = body.Length <= 2000 ? body : body[..2000] + "…";
                _logger.LogWarning(
                    "OpenAI-compatible chat returned {StatusCode} from {Url}. Body: {Body}",
                    (int)response.StatusCode,
                    httpRequest.RequestUri,
                    preview);
            }

            throw new HttpRequestException(body, null, response.StatusCode);
        }

        var openAiResponse = await response.Content
            .ReadFromJsonAsync<OpenAiChatResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return MapResponse(request.Model, openAiResponse);
    }

    public async IAsyncEnumerable<OllamaResponse> ChatStreamAsync(
        OllamaRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = MapRequest(request, stream: true);
        using var httpRequest = CreateRequest(payload);
        using var response = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(body, null, response.StatusCode);
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..].Trim();
            if (data == "[DONE]")
                yield break;

            var chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, JsonOptions);
            var choice = chunk?.Choices?.FirstOrDefault();
            var delta = choice?.Delta;
            var content = delta?.Content;
            if (string.IsNullOrEmpty(content) && (delta?.ToolCalls is null || delta.ToolCalls.Count == 0))
                continue;

            yield return new OllamaResponse
            {
                Model = request.Model,
                Message = new OllamaMessage
                {
                    Role = "assistant",
                    Content = content ?? string.Empty,
                    ToolCalls = MapToolCallsFromOpenAi(delta?.ToolCalls)
                },
                Done = false
            };
        }

        yield return new OllamaResponse
        {
            Model = request.Model,
            Message = new OllamaMessage { Role = "assistant", Content = string.Empty },
            Done = true
        };
    }

    public async Task<OllamaResponse> GenerateAsync(OllamaGenerateRequest request, CancellationToken cancellationToken)
    {
        var chatRequest = ToChatRequest(request, stream: false);
        var chatResponse = await ChatAsync(chatRequest, cancellationToken).ConfigureAwait(false);
        return ToGenerateResponse(chatResponse);
    }

    public async IAsyncEnumerable<OllamaResponse> GenerateStreamAsync(
        OllamaGenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chatRequest = ToChatRequest(request, stream: true);

        await foreach (var chunk in ChatStreamAsync(chatRequest, cancellationToken).ConfigureAwait(false))
        {
            var text = chunk.Message?.Content ?? chunk.Response ?? string.Empty;
            yield return new OllamaResponse
            {
                Model = chunk.Model,
                Response = text,
                Done = chunk.Done
            };
        }
    }

    internal static OllamaRequest ToChatRequest(OllamaGenerateRequest request, bool stream) =>
        new()
        {
            Model = request.Model,
            Messages = [new OllamaMessage { Role = "user", Content = request.Prompt }],
            Stream = stream,
            Options = request.Options,
            Format = request.Format,
            KeepAlive = request.KeepAlive,
            Think = request.Think
        };

    private static OllamaResponse ToGenerateResponse(OllamaResponse chatResponse) =>
        chatResponse with
        {
            Response = chatResponse.Message?.Content ?? chatResponse.Response ?? string.Empty,
            Message = null
        };

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
            ApplyAuth(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private HttpRequestMessage CreateRequest(OpenAiChatRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        ApplyAuth(request);
        return request;
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    internal static OpenAiChatRequest MapRequest(OllamaRequest request, bool stream)
    {
        var pendingToolCallIds = new Queue<string>();
        var messages = new List<OpenAiChatMessage>();

        foreach (var m in request.Messages)
        {
            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var toolCallId = pendingToolCallIds.Count > 0
                    ? pendingToolCallIds.Dequeue()
                    : $"call_{messages.Count}";
                messages.Add(new OpenAiChatMessage
                {
                    Role = "tool",
                    Content = m.Content ?? string.Empty,
                    ToolCallId = toolCallId
                });
                continue;
            }

            List<OpenAiToolCall>? toolCalls = null;
            if (m.ToolCalls is { Count: > 0 })
            {
                toolCalls = new List<OpenAiToolCall>(m.ToolCalls.Count);
                for (var i = 0; i < m.ToolCalls.Count; i++)
                {
                    var tc = m.ToolCalls[i];
                    var id = $"call_{messages.Count}_{i}";
                    pendingToolCallIds.Enqueue(id);
                    toolCalls.Add(new OpenAiToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new OpenAiFunctionCall
                        {
                            Name = tc.Function.Name,
                            Arguments = string.IsNullOrWhiteSpace(tc.Function.Arguments)
                                ? "{}"
                                : tc.Function.Arguments
                        }
                    });
                }
            }

            messages.Add(new OpenAiChatMessage
            {
                Role = m.Role,
                Content = toolCalls is { Count: > 0 } && string.IsNullOrEmpty(m.Content) ? null : m.Content,
                ToolCalls = toolCalls
            });
        }

        List<OpenAiTool>? tools = null;
        if (request.Tools is { Count: > 0 })
        {
            tools = request.Tools.Select(t => new OpenAiTool
            {
                Type = string.IsNullOrWhiteSpace(t.Type) ? "function" : t.Type,
                Function = new OpenAiFunction
                {
                    Name = t.Function.Name,
                    Description = t.Function.Description,
                    Parameters = t.Function.Parameters
                }
            }).ToList();
        }

        return new OpenAiChatRequest
        {
            Model = request.Model,
            Stream = stream,
            Messages = messages,
            Tools = tools,
            ToolChoice = NormalizeToolChoice(request.ToolChoice, tools),
            ResponseFormat = MapResponseFormat(request.Format),
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            MaxTokens = request.Options?.NumPredict,
            Stop = request.Options?.Stop,
            Seed = request.Options?.Seed,
            // Native `think` is ignored on /v1; this is the supported switch.
            ReasoningEffort = request.Think == false ? "none" : null,
            // Forward Ollama-native options (num_ctx, top_k, …) for Ollama /v1 servers.
            Options = HasOllamaNativeOptions(request.Options) ? request.Options : null
        };
    }

    private static string? NormalizeToolChoice(string? toolChoice, List<OpenAiTool>? tools)
    {
        if (tools is null || tools.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(toolChoice))
            return null;

        var normalized = toolChoice.Trim().ToLowerInvariant();
        return normalized is "auto" or "required" or "none" ? normalized : null;
    }

    private static OpenAiResponseFormat? MapResponseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;

        var f = format.Trim();
        if (f.Equals("json", StringComparison.OrdinalIgnoreCase)
            || f.Equals("json_object", StringComparison.OrdinalIgnoreCase)
            || f.Equals("{\"type\":\"json_object\"}", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAiResponseFormat { Type = "json_object" };
        }

        return null;
    }

    private static bool HasOllamaNativeOptions(OllamaOptions? o) =>
        o is not null
        && (o.NumCtx is not null
            || o.TopK is not null
            || o.RepeatPenalty is not null
            || o.TfsZ is not null
            || o.Mirostat is not null
            || o.NumPredict is not null
            || o.Temperature is not null
            || o.TopP is not null);

    private static OllamaResponse MapResponse(string model, OpenAiChatResponse? response)
    {
        var choice = response?.Choices?.FirstOrDefault();
        var message = choice?.Message;
        var content = message?.Content ?? string.Empty;
        var toolCalls = MapToolCallsFromOpenAi(message?.ToolCalls);
        var finish = choice?.FinishReason;
        var doneReason = string.Equals(finish, "tool_calls", StringComparison.OrdinalIgnoreCase)
            ? "tool_calls"
            : (finish ?? "stop");

        return new OllamaResponse
        {
            Model = string.IsNullOrWhiteSpace(response?.Model) ? model : response!.Model!,
            Message = new OllamaMessage
            {
                Role = "assistant",
                Content = content,
                ToolCalls = toolCalls
            },
            Response = content,
            Done = true,
            DoneReason = doneReason
        };
    }

    private static List<OllamaToolCall>? MapToolCallsFromOpenAi(List<OpenAiToolCall>? toolCalls)
    {
        if (toolCalls is null || toolCalls.Count == 0)
            return null;

        return toolCalls
            .Where(tc => !string.IsNullOrWhiteSpace(tc.Function?.Name))
            .Select(tc => new OllamaToolCall(
                new OllamaFunctionCall(
                    tc.Function!.Name,
                    string.IsNullOrWhiteSpace(tc.Function.Arguments) ? "{}" : tc.Function.Arguments)))
            .ToList();
    }
}
