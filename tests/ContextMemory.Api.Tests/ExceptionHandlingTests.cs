using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class ExceptionHandlingTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExceptionHandlingTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Register_WithMissingFields_Returns400Json()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/apps/register");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        request.Content = JsonContent.Create(new RegisterAppRequest { AppName = "", Domain = "" });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class OllamaMessageExtensionsTests
{
    [Fact]
    public void GetLastUserMessage_ReturnsLastUserRoleMessage()
    {
        var request = new OllamaRequest
        {
            Messages =
            [
                new OllamaMessage { Role = "system", Content = "sys" },
                new OllamaMessage { Role = "user", Content = "first" },
                new OllamaMessage { Role = "assistant", Content = "reply" },
                new OllamaMessage { Role = "user", Content = "last" }
            ]
        };

        Assert.Equal("last", request.GetLastUserMessage()?.Content);
    }
}
