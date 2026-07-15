using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class ContractTests : IClassFixture<StubOllamaWebApplicationFactory>
{
    private const string AppId = "demo-app";
    private const string ApiKey = "test-api-key";
    private readonly HttpClient _client;
    private readonly StubOllamaWebApplicationFactory _factory;

    public ContractTests(StubOllamaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-App-Id", AppId);
        request.Headers.Add("X-User-Id", "contract-user");
        request.Headers.Add("X-Session-Id", "contract-session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return request;
    }

    [Fact]
    public async Task Chat_Passthrough_PreservesOllamaResponseFields()
    {
        var payload = """
            {
              "model": "qwen3.5:9b",
              "messages": [{ "role": "user", "content": "hello" }],
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/chat",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("llama3.2", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("done").GetBoolean());
        Assert.Equal("stop", root.GetProperty("done_reason").GetString());
        Assert.Equal("Hello from stub", root.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(4321000000, root.GetProperty("total_duration").GetInt64());
        Assert.Equal(312, root.GetProperty("prompt_eval_count").GetInt32());
        Assert.Equal(187, root.GetProperty("eval_count").GetInt32());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("context").ValueKind);
        Assert.True(response.Headers.Contains("X-Session-Id"));
    }

    [Fact]
    public async Task Generate_Passthrough_PreservesOllamaGenerateFields()
    {
        var payload = """
            {
              "model": "qwen3.5:9b",
              "prompt": "Summarize this",
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/generate",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("llama3.2", root.GetProperty("model").GetString());
        Assert.Equal("Generated text", root.GetProperty("response").GetString());
        Assert.True(root.GetProperty("done").GetBoolean());
        Assert.Equal(4321000000, root.GetProperty("total_duration").GetInt64());
        Assert.Equal(12, root.GetProperty("prompt_eval_count").GetInt32());
        Assert.Equal(8, root.GetProperty("eval_count").GetInt32());
    }

    [Fact]
    public async Task Generate_Streaming_ReturnsNdjson()
    {
        var payload = """{"model":"qwen3.5:9b","prompt":"x","stream":true}""";
        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/generate",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Assert.Contains("ndjson", response.Content.Headers.ContentType?.MediaType ?? "");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"response\":\"Generated\"", body);
        Assert.Contains("\"done\":true", body);
    }

    [Fact]
    public async Task GetApp_ReturnsMetadata()
    {
        using var request = CreateAuthedRequest(HttpMethod.Get, $"/apps/{AppId}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(AppId, root.GetProperty("AppId").GetString());
        Assert.Equal("seed", root.GetProperty("Source").GetString());
        Assert.Equal("ollama", root.GetProperty("LlmBackend").GetString());
        Assert.True(root.TryGetProperty("ActiveUsers", out _));
    }

    [Fact]
    public async Task Chat_PersistsMessagesPerSession()
    {
        var payload = """
            {
              "model": "qwen3.5:9b",
              "messages": [{ "role": "user", "content": "remember this" }],
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/chat",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var snapshot = await sessions.LoadAsync(AppId, "contract-user", "contract-session");
        var history = snapshot.Messages;

        Assert.Contains(history, m => m.Role == "user" && m.Content.Contains("remember this", StringComparison.Ordinal));
        Assert.Contains(history, m => m.Role == "assistant");
    }

    [Fact]
    public async Task Chat_UpdatesSessionWiki_AfterTurn()
    {
        const string sessionId = "wiki-maintainer-session";
        var payload = """
            {
              "model": "qwen3.5:9b",
              "messages": [{ "role": "user", "content": "guarda este facto" }],
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/chat",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        request.Headers.Remove("X-Session-Id");
        request.Headers.Add("X-Session-Id", sessionId);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await Task.Delay(1500);

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var snapshot = await sessions.LoadAsync(AppId, "contract-user", sessionId);

        Assert.Contains("## [", snapshot.LogMd, StringComparison.Ordinal);
        Assert.True(snapshot.Pages.ContainsKey("stub-fact"));
    }
}
