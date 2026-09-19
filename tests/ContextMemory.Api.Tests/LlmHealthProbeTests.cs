using System.Net;
using ContextMemory.Adapters.OpenAi;
using Xunit;

namespace ContextMemory.Api.Tests;

public class LlmHealthProbeTests
{
    [Fact]
    public async Task OpenAiChatClient_IsHealthy_PingsOrigin_NotModels()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = new OpenAiChatClient(http, "https://server.kortexio.io/v1", apiKey: null);

        Assert.True(await client.IsHealthyAsync(CancellationToken.None));
        Assert.Equal("https://server.kortexio.io/", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task OpenAiChatClient_IsHealthy_Treats401AsReachable()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var client = new OpenAiChatClient(http, "https://api.openai.com/v1", apiKey: "sk-test");

        Assert.True(await client.IsHealthyAsync(CancellationToken.None));
        Assert.DoesNotContain("/models", handler.LastUri?.AbsolutePath ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
