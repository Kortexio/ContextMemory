using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class IntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ContextMemoryWebApplicationFactory _factory;

    public IntegrationTests(ContextMemoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsJsonWithStatus()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("status", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checks", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ollama", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [Fact]
    public async Task Chat_WithoutHeaders_Returns401()
    {
        var response = await _client.PostAsync("/api/chat", JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithoutMasterKey_Returns401()
    {
        var response = await _client.GetAsync("/admin/apps");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithMasterKey_ReturnsApps()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/apps");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SessionMemory_IsolatesSessions()
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();

        await sessions.AppendMessagesAsync(
            "demo-dev",
            "user-a",
            "session-1",
            [new OllamaMessage { Role = "user", Content = "segredo-s1" }],
            maxMessages: 20);

        await sessions.AppendMessagesAsync(
            "demo-dev",
            "user-a",
            "session-2",
            [new OllamaMessage { Role = "user", Content = "segredo-s2" }],
            maxMessages: 20);

        var snapshot1 = await sessions.LoadAsync("demo-dev", "user-a", "session-1");
        var snapshot2 = await sessions.LoadAsync("demo-dev", "user-a", "session-2");

        Assert.Contains(snapshot1.Messages, m => m.Content.Contains("segredo-s1", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot1.Messages, m => m.Content.Contains("segredo-s2", StringComparison.Ordinal));
        Assert.Contains(snapshot2.Messages, m => m.Content.Contains("segredo-s2", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot2.Messages, m => m.Content.Contains("segredo-s1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chat_RequestBody_IsOllamaCompatibleShape()
    {
        var payload = """
            {
              "model": "qwen3.5:9b",
              "messages": [{ "role": "user", "content": "olá" }],
              "stream": false
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-App-Id", "demo-dev");
        request.Headers.Add("X-User-Id", "contract-user");
        request.Headers.Add("X-Session-Id", "test-session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Session-Id"));
    }
}
