using System.Net;
using System.Net.Http.Headers;
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
    private readonly string? _apiKey;

    public OllamaAdapter(HttpClient httpClient, IOptions<OllamaAdapterOptions> options)
        : this(httpClient, options.Value.OllamaEndpoint, apiKey: null)
    {
    }

    private OllamaAdapter(HttpClient httpClient, string baseUrl, string? apiKey)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>Returns this instance or a copy pointed at a per-app endpoint/API key.</summary>
    public OllamaAdapter WithConnection(string? endpointOverride, string? apiKeyOverride)
    {
        if (string.IsNullOrWhiteSpace(endpointOverride) && string.IsNullOrWhiteSpace(apiKeyOverride))
            return this;

        return new OllamaAdapter(
            _httpClient,
            string.IsNullOrWhiteSpace(endpointOverride) ? _baseUrl : endpointOverride,
            string.IsNullOrWhiteSpace(apiKeyOverride) ? _apiKey : apiKeyOverride);
    }

    public async Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateJsonRequest(HttpMethod.Post, $"{_baseUrl}/api/chat", request);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

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
        using var httpRequest = CreateJsonRequest(HttpMethod.Post, $"{_baseUrl}/api/chat", streamRequest);

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
        using var httpRequest = CreateJsonRequest(HttpMethod.Post, $"{_baseUrl}/api/generate", request);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

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
        using var httpRequest = CreateJsonRequest(HttpMethod.Post, $"{_baseUrl}/api/generate", streamRequest);

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
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
            ApplyAuth(httpRequest);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string url, T body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        ApplyAuth(request);
        return request;
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_apiKey is null)
            return;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
