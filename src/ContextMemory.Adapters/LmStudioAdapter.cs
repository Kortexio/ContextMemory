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
    private readonly OpenAiChatClient _client;

    public LmStudioAdapter(HttpClient httpClient, IOptions<LmStudioAdapterOptions> options)
    {
        var baseUrl = $"{options.Value.LmStudioEndpoint.TrimEnd('/')}/v1";
        _client = new OpenAiChatClient(httpClient, baseUrl, apiKey: null);
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
