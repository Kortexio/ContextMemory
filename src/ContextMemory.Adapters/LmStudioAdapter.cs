using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Adapters.OpenAi;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class LmStudioAdapterOptions
{
    public const string SectionName = "ContextMemory";
    public string LmStudioEndpoint { get; set; } = "http://localhost:1234";
}

public sealed class LmStudioAdapter : ILlmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _defaultBaseUrl;
    private readonly OpenAiChatClient _client;

    public LmStudioAdapter(HttpClient httpClient, IOptions<LmStudioAdapterOptions> options)
    {
        _httpClient = httpClient;
        _defaultBaseUrl = OpenAiAdapter.NormalizeOpenAiBase(options.Value.LmStudioEndpoint);
        _client = new OpenAiChatClient(httpClient, _defaultBaseUrl, apiKey: null);
    }

    private LmStudioAdapter(HttpClient httpClient, string baseUrl, string? apiKey, string defaultBaseUrl)
    {
        _httpClient = httpClient;
        _defaultBaseUrl = defaultBaseUrl;
        _client = new OpenAiChatClient(httpClient, baseUrl, apiKey);
    }

    public LmStudioAdapter WithConnection(string? endpointOverride, string? apiKeyOverride)
    {
        if (string.IsNullOrWhiteSpace(endpointOverride) && string.IsNullOrWhiteSpace(apiKeyOverride))
            return this;

        var baseUrl = string.IsNullOrWhiteSpace(endpointOverride)
            ? _defaultBaseUrl
            : OpenAiAdapter.NormalizeOpenAiBase(endpointOverride);
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? null : apiKeyOverride.Trim();
        return new LmStudioAdapter(_httpClient, baseUrl, apiKey, _defaultBaseUrl);
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
}
