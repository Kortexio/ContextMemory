using ContextMemory.Core.Contracts;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using ContextMemory.Adapters.OpenAi;
using Microsoft.Extensions.Options;

namespace ContextMemory.Adapters;

public sealed class OpenAiAdapter : ILlmAdapter
{
    private readonly OpenAiChatClient _client;

    public OpenAiAdapter(HttpClient httpClient, IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        var baseUrl = $"{config.OpenAiEndpoint.TrimEnd('/')}/v1";
        _client = new OpenAiChatClient(httpClient, baseUrl, config.OpenAiApiKey);
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
