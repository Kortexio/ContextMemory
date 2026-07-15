using System.Net;
using System.Net.Http.Headers;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

/// <summary>
/// Smoke tests against a real Ollama instance. Set OLLAMA_E2E=1 to enable (CI uses service container).
/// </summary>
[Trait("Category", "OllamaE2E")]
public class OllamaE2ETests : IClassFixture<OllamaE2EWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly OllamaE2EWebApplicationFactory _factory;

    public OllamaE2ETests(OllamaE2EWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("OLLAMA_E2E"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task Health_WhenOllamaRunning_ReportsOllamaUp()
    {
        if (!IsEnabled)
            return;

        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("up", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_WhenOllamaAndModelAvailable_ReturnsAssistantMessage()
    {
        if (!IsEnabled)
            return;

        var model = Environment.GetEnvironmentVariable("OLLAMA_E2E_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            return;

        var payload = $$"""
            {
              "model": "{{model}}",
              "messages": [{ "role": "user", "content": "Say hi in one word." }],
              "stream": false
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-App-Id", "demo-dev");
        request.Headers.Add("X-User-Id", "e2e-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("message", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_WhenOllamaAvailable_UpdatesSessionLogAfterTurn()
    {
        if (!IsEnabled)
            return;

        var model = Environment.GetEnvironmentVariable("OLLAMA_E2E_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            return;

        const string sessionId = "e2e-wiki-session";
        var payload = $$"""
            {
              "model": "{{model}}",
              "messages": [{ "role": "user", "content": "Diz apenas: E2E-WIKI-OK" }],
              "stream": false
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-App-Id", "demo-dev");
        request.Headers.Add("X-User-Id", "e2e-wiki-user");
        request.Headers.Add("X-Session-Id", sessionId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(15));

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var snapshot = await sessions.LoadAsync("demo-dev", "e2e-wiki-user", sessionId);

        Assert.Contains("## [", snapshot.LogMd, StringComparison.Ordinal);
    }
}
