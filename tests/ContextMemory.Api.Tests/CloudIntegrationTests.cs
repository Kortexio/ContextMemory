using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CloudIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ContextMemoryWebApplicationFactory _factory;

    public CloudIntegrationTests(ContextMemoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DeactivateApp_Returns403OnChatWithValidKey()
    {
        using var scope = _factory.Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IAppRegistrationService>();
        var registered = await registration.RegisterAsync(new RegisterAppRequest
        {
            AppName = "Deactivate Test",
            Domain = "deact-test"
        });

        using var deactivate = new HttpRequestMessage(HttpMethod.Delete, $"/admin/apps/{registered.AppId}");
        deactivate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        var deactivateResponse = await _client.SendAsync(deactivate);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        using var chat = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        chat.Headers.Add("X-App-Id", registered.AppId);
        chat.Headers.Add("X-User-Id", "user-1");
        chat.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registered.ApiKey);
        chat.Content = JsonContent.Create(new
        {
            model = "qwen3.5:9b",
            messages = new[] { new { role = "user", content = "olá" } }
        });

        var chatResponse = await _client.SendAsync(chat);
        Assert.Equal(HttpStatusCode.Forbidden, chatResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSessionsForUser_RemovesSessionData()
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();

        await sessions.EnsureInitializedAsync("demo-dev", "gdpr-user", "sess-gdpr", "schema", CancellationToken.None);
        await sessions.AppendMessagesAsync(
            "demo-dev",
            "gdpr-user",
            "sess-gdpr",
            [new OllamaMessage { Role = "user", Content = "dado sensível" }],
            maxMessages: 20);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/admin/sessions/demo-dev/gdpr-user");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        var deleteResponse = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var snapshot = await sessions.LoadAsync("demo-dev", "gdpr-user", "sess-gdpr");
        Assert.Empty(snapshot.Messages);
    }

    [Fact]
    public async Task DeleteSessionsOlderThan_ReturnsDeletedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();

        await sessions.EnsureInitializedAsync("demo-dev", "retention-user", "sess-old", "schema", CancellationToken.None);
        await sessions.AppendMessagesAsync(
            "demo-dev",
            "retention-user",
            "sess-old",
            [new OllamaMessage { Role = "user", Content = "antigo" }],
            maxMessages: 20);

        var cutoff = DateTimeOffset.UtcNow.AddHours(1).ToString("o");
        using var delete = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/admin/sessions?appId=demo-dev&olderThan={Uri.EscapeDataString(cutoff)}");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        var deleteResponse = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var body = await deleteResponse.Content.ReadFromJsonAsync<DeleteSessionsResponse>();
        Assert.NotNull(body);
        Assert.True(body.DeletedCount >= 1);
    }

    [Fact]
    public async Task SelfHostedSandboxExecutor_RunsViaMockEndpoint()
    {
        using var scope = _factory.Services.CreateScope();
        var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();

        await configStore.UpdateAsync(
            "demo-dev",
            new AppConfigPatchRequest
            {
                Agentic = new AgenticConfig
                {
                    Enabled = true,
                    Tools = new AgenticToolsConfig
                    {
                        Execution =
                        [
                            new ExecutionToolConfig
                            {
                                Type = "self-hosted-sandbox",
                                Runtime = "python",
                                SandboxEndpoint = "mock://sandbox"
                            }
                        ]
                    }
                }
            });

        var executors = scope.ServiceProvider.GetServices<IToolExecutor>().ToList();
        var selfHosted = executors.OfType<SelfHostedGVisorExecutor>().Single();
        var config = configStore.GetConfig("demo-dev");

        Assert.True(selfHosted.CanExecute("python_execute", config));

        var result = await selfHosted.ExecuteAsync(
            new OllamaToolCall(new OllamaFunctionCall("python_execute", """{"code":"print(1)"}""")),
            "demo-dev",
            config);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("mock sandbox", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_McpCredentialAndCatalogEndpoints_Work()
    {
        using var scope = _factory.Services.CreateScope();
        var configStore = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();

        await configStore.UpdateAsync(
            "demo-dev",
            new AppConfigPatchRequest
            {
                Agentic = new AgenticConfig
                {
                    Enabled = true,
                    Tools = new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora-mcp",
                                Url = "mock://zuora",
                                Enabled = true,
                                CredentialRef = "zuora-prod",
                                ToolAllowlist = ["get_account"],
                                RequiresConfirmation = ["get_account"]
                            }
                        ]
                    }
                }
            });

        using var cred = new HttpRequestMessage(HttpMethod.Post, "/admin/apps/demo-dev/mcp/credentials/zuora-mcp");
        cred.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        cred.Content = JsonContent.Create(new McpCredentialUpsertRequest
        {
            IntegrationName = "zuora-mcp",
            CredentialRef = "zuora-prod",
            AuthMode = "bearer",
            BearerToken = "secret"
        });

        var credResponse = await _client.SendAsync(cred);
        Assert.Equal(HttpStatusCode.NoContent, credResponse.StatusCode);

        using var listed = new HttpRequestMessage(HttpMethod.Get, "/admin/apps/demo-dev/mcp/credentials/zuora-mcp");
        listed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        var listedResponse = await _client.SendAsync(listed);
        Assert.Equal(HttpStatusCode.OK, listedResponse.StatusCode);
        var listedBody = await listedResponse.Content.ReadFromJsonAsync<List<McpCredentialAdminDto>>();
        Assert.NotNull(listedBody);
        var stored = Assert.Single(listedBody!, x => x.CredentialRef == "zuora-prod");
        Assert.Equal("bearer", stored.AuthMode);
        Assert.Equal("secret", stored.BearerToken);

        using var sync = new HttpRequestMessage(HttpMethod.Post, "/admin/apps/demo-dev/mcp/catalog/rebuild");
        sync.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        sync.Content = JsonContent.Create(new McpCatalogSyncRequest { IntegrationName = "zuora-mcp" });

        var syncResponse = await _client.SendAsync(sync);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var syncBody = await syncResponse.Content.ReadFromJsonAsync<List<McpCatalogSyncResult>>();
        Assert.NotNull(syncBody);
        Assert.Contains(syncBody!, x => x.IntegrationName == "zuora-mcp");

        using var servers = new HttpRequestMessage(HttpMethod.Get, "/admin/apps/demo-dev/mcp/servers");
        servers.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        var serversResponse = await _client.SendAsync(servers);
        Assert.Equal(HttpStatusCode.OK, serversResponse.StatusCode);
    }

    private sealed class DeleteSessionsResponse
    {
        public int DeletedCount { get; init; }
    }
}
