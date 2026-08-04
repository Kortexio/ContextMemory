using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Adapters.OpenAi;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class OpenAiCompatibleContractTests : IClassFixture<StubOllamaWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly StubOllamaWebApplicationFactory _factory;

    public OpenAiCompatibleContractTests(StubOllamaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChatCompletions_ReturnsOpenAiShape()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test-api-key");
        request.Headers.TryAddWithoutValidation("X-App-Id", "demo-app");
        request.Headers.TryAddWithoutValidation("X-User-Id", "user-1");
        request.Content = JsonContent.Create(new
        {
            model = "qwen3.5:9b",
            stream = false,
            messages = new[]
            {
                new { role = "user", content = "Hello" }
            }
        });

        using var response = await _client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
        Assert.Equal(
            "Hello from stub",
            choices[0].GetProperty("message").GetProperty("content").GetString());
        Assert.False(doc.RootElement.TryGetProperty("message", out _), "Must not use Ollama wire shape");
    }

    [Fact]
    public async Task Models_ReturnsConfiguredModel()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test-api-key");
        request.Headers.TryAddWithoutValidation("X-App-Id", "demo-app");
        request.Headers.TryAddWithoutValidation("X-User-Id", "user-1");

        using var response = await _client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OpenAiCompatibleModelsResponse>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Data, m => m.Id == "qwen3.5:9b");
    }

    [Fact]
    public void MapRequest_IncludesToolsAndOptions()
    {
        var request = new OllamaRequest
        {
            Model = "m",
            Messages =
            [
                new OllamaMessage { Role = "user", Content = "hi" }
            ],
            Tools =
            [
                new OllamaTool("function", new OllamaFunction("wiki_search", "Search", new { type = "object" }))
            ],
            Options = new OllamaOptions { Temperature = 0.2f, NumPredict = 128 }
        };

        var mapped = OpenAiChatClient.MapRequest(request, stream: false);
        Assert.Equal(0.2f, mapped.Temperature);
        Assert.Equal(128, mapped.MaxTokens);
        Assert.NotNull(mapped.Tools);
        Assert.Single(mapped.Tools!);
        Assert.Equal("wiki_search", mapped.Tools![0].Function.Name);
    }

    [Fact]
    public void MapRequest_ThinkFalse_SetsReasoningEffortNone()
    {
        var request = new OllamaRequest
        {
            Model = "qwen3.5:9b",
            Messages = [new OllamaMessage { Role = "user", Content = "hi" }],
            Think = false
        };

        var mapped = OpenAiChatClient.MapRequest(request, stream: false);
        Assert.Equal("none", mapped.ReasoningEffort);
    }

    [Fact]
    public void MapRequest_ThinkTrue_OmitsReasoningEffort()
    {
        var request = new OllamaRequest
        {
            Model = "qwen3.5:9b",
            Messages = [new OllamaMessage { Role = "user", Content = "hi" }],
            Think = true
        };

        var mapped = OpenAiChatClient.MapRequest(request, stream: false);
        Assert.Null(mapped.ReasoningEffort);
    }
}
