using ContextMemory.Core.Contracts;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using ContextMemory.Adapters.OpenAi;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class OpenAiAdapter : ILlmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _defaultBaseUrl;
    private readonly string? _defaultApiKey;
    private readonly OpenAiChatClient _client;

    public OpenAiAdapter(HttpClient httpClient, IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _httpClient = httpClient;
        _defaultBaseUrl = NormalizeOpenAiBase(config.OpenAiEndpoint);
        _defaultApiKey = string.IsNullOrWhiteSpace(config.OpenAiApiKey) ? null : config.OpenAiApiKey.Trim();
        _client = new OpenAiChatClient(_httpClient, _defaultBaseUrl, _defaultApiKey);
    }

    private OpenAiAdapter(HttpClient httpClient, string baseUrl, string? apiKey, string defaultBaseUrl, string? defaultApiKey)
    {
        _httpClient = httpClient;
        _defaultBaseUrl = defaultBaseUrl;
        _defaultApiKey = defaultApiKey;
        _client = new OpenAiChatClient(httpClient, baseUrl, apiKey);
    }

    public OpenAiAdapter WithConnection(string? endpointOverride, string? apiKeyOverride)
    {
        if (string.IsNullOrWhiteSpace(endpointOverride) && string.IsNullOrWhiteSpace(apiKeyOverride))
            return this;

        var baseUrl = string.IsNullOrWhiteSpace(endpointOverride)
            ? _defaultBaseUrl
            : NormalizeOpenAiBase(endpointOverride);
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? _defaultApiKey : apiKeyOverride.Trim();
        return new OpenAiAdapter(_httpClient, baseUrl, apiKey, _defaultBaseUrl, _defaultApiKey);
    }

    public Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken = default) =>
        _client.ChatAsync(request, cancellationToken);

    public IAsyncEnumerable<OllamaResponse> ChatStreamAsync(OllamaRequest request, CancellationToken cancellationToken = default) =>
        _client.ChatStreamAsync(request, cancellationToken);

    public Task<OllamaResponse> GenerateAsync(OllamaGenerateRequest request, CancellationToken cancellationToken = default) =>
        _client.GenerateAsync(request, cancellationToken);

    public IAsyncEnumerable<OllamaResponse> GenerateStreamAsync(OllamaGenerateRequest request, CancellationToken cancellationToken = default) =>
        _client.GenerateStreamAsync(request, cancellationToken);

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        _client.IsHealthyAsync(cancellationToken);

    internal static string NormalizeOpenAiBase(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return $"{trimmed}/v1";
    }
}
