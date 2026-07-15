using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ContextMemory.Core.Models;

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

    public OpenAiChatClient(HttpClient httpClient, string baseUrl, string? apiKey)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken)
    {
        var payload = MapRequest(request, stream: false);
        using var httpRequest = CreateRequest(payload);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (string.IsNullOrEmpty(content))
                continue;

            yield return new OllamaResponse
            {
                Model = request.Model,
                Message = new OllamaMessage { Role = "assistant", Content = content },
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
        var chatRequest = new OllamaRequest
        {
            Model = request.Model,
            Messages = [new OllamaMessage { Role = "user", Content = request.Prompt }],
            Stream = false,
            Options = request.Options
        };

        var chatResponse = await ChatAsync(chatRequest, cancellationToken).ConfigureAwait(false);
        return ToGenerateResponse(chatResponse);
    }

    public async IAsyncEnumerable<OllamaResponse> GenerateStreamAsync(
        OllamaGenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chatRequest = new OllamaRequest
        {
            Model = request.Model,
            Messages = [new OllamaMessage { Role = "user", Content = request.Prompt }],
            Stream = true,
            Options = request.Options
        };

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

    private static OpenAiChatRequest MapRequest(OllamaRequest request, bool stream) =>
        new()
        {
            Model = request.Model,
            Stream = stream,
            Messages = request.Messages
                .Select(m => new OpenAiChatMessage { Role = m.Role, Content = m.Content })
                .ToList()
        };

    private static OllamaResponse MapResponse(string model, OpenAiChatResponse? response)
    {
        var content = response?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        return new OllamaResponse
        {
            Model = model,
            Message = new OllamaMessage { Role = "assistant", Content = content },
            Response = content,
            Done = true,
            DoneReason = "stop"
        };
    }
}
