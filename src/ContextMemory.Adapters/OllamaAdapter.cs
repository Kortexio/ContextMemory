using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class OllamaAdapterOptions
{
    public const string SectionName = "ContextMemory";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
}

public sealed class OllamaAdapter : ILlmAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public OllamaAdapter(HttpClient httpClient, IOptions<OllamaAdapterOptions> options)
    {
        _httpClient = httpClient;
        _baseUrl = options.Value.OllamaEndpoint.TrimEnd('/');
    }

    public async Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync($"{_baseUrl}/api/chat", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(body, null, response.StatusCode);
        }

        return await response.Content
            .ReadFromJsonAsync<OllamaResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty response from Ollama.");
    }

    public async IAsyncEnumerable<OllamaResponse> ChatStreamAsync(
        OllamaRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streamRequest = request with { Stream = true };
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = JsonContent.Create(streamRequest, options: JsonOptions)
        };

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
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var chunk = JsonSerializer.Deserialize<OllamaResponse>(line, JsonOptions);
            if (chunk is not null)
                yield return chunk;
        }
    }

    public async Task<OllamaResponse> GenerateAsync(
        OllamaGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync($"{_baseUrl}/api/generate", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(body, null, response.StatusCode);
        }

        return await response.Content
            .ReadFromJsonAsync<OllamaResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty response from Ollama.");
    }

    public async IAsyncEnumerable<OllamaResponse> GenerateStreamAsync(
        OllamaGenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streamRequest = request with { Stream = true };
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = JsonContent.Create(streamRequest, options: JsonOptions)
        };

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
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var chunk = JsonSerializer.Deserialize<OllamaResponse>(line, JsonOptions);
            if (chunk is not null)
                yield return chunk;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"{_baseUrl}/api/tags", cancellationToken)
                .ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }
}
