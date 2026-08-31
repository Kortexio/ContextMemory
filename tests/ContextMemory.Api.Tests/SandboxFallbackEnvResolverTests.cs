using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SandboxFallbackEnvResolverTests
{
    [Fact]
    public async Task Resolve_MergesAzureAndGithubCredentialEnv()
    {
        var store = new FakeCredentialStore();
        store.Put(new McpCredentialRecord
        {
            AppId = "app",
            IntegrationName = "azure-monitor",
            CredentialRef = "azure-sp",
            AuthMode = "env",
            Env = new Dictionary<string, string>
            {
                ["AZURE_TENANT_ID"] = "tenant",
                ["AZURE_CLIENT_ID"] = "client",
                ["AZURE_CLIENT_SECRET"] = "secret"
            },
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        store.Put(new McpCredentialRecord
        {
            AppId = "app",
            IntegrationName = "github",
            CredentialRef = "gh-pat",
            AuthMode = "env",
            Env = new Dictionary<string, string> { ["GITHUB_TOKEN"] = "ghp_test" },
            UpdatedAt = DateTimeOffset.UnixEpoch
        });

        var config = new AppRuntimeConfig
        {
            AppId = "app",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = new AgenticToolsConfig
                {
                    Integrations =
                    [
                        new IntegrationToolConfig
                        {
                            Name = "azure-monitor",
                            Type = "mcp",
                            Command = "node",
                            CredentialRef = "azure-sp",
                            Enabled = true
                        },
                        new IntegrationToolConfig
                        {
                            Name = "github",
                            Type = "mcp",
                            Command = "npx",
                            CredentialRef = "gh-pat",
                            Enabled = true
                        },
                        new IntegrationToolConfig
                        {
                            Name = "zuora",
                            Type = "mcp",
                            Url = "http://zuora",
                            CredentialRef = "z",
                            Enabled = true,
                            Env = new Dictionary<string, string> { ["SHOULD_NOT"] = "appear" }
                        }
                    ]
                }
            }
        };

        var env = await SandboxFallbackEnvResolver.ResolveAsync("app", config, store);

        Assert.Equal("tenant", env["AZURE_TENANT_ID"]);
        Assert.Equal("client", env["AZURE_CLIENT_ID"]);
        Assert.Equal("secret", env["AZURE_CLIENT_SECRET"]);
        Assert.Equal("ghp_test", env["GITHUB_TOKEN"]);
        Assert.False(env.ContainsKey("SHOULD_NOT"));
    }

    [Fact]
    public void IsConventionalName_MatchesAliases()
    {
        Assert.True(SandboxFallbackEnvResolver.IsConventionalName("azure-monitor"));
        Assert.True(SandboxFallbackEnvResolver.IsConventionalName("github"));
        Assert.True(SandboxFallbackEnvResolver.IsConventionalName("git"));
        Assert.False(SandboxFallbackEnvResolver.IsConventionalName("zuora"));
    }

    private sealed class FakeCredentialStore : IMcpCredentialStore
    {
        private readonly Dictionary<string, McpCredentialRecord> _map = new(StringComparer.OrdinalIgnoreCase);

        public void Put(McpCredentialRecord record) =>
            _map[$"{record.AppId}|{record.IntegrationName}|{record.CredentialRef}"] = record;

        public Task<McpCredentialRecord?> GetAsync(
            string appId,
            string integrationName,
            string? credentialRef,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(credentialRef))
                return Task.FromResult<McpCredentialRecord?>(null);

            _map.TryGetValue($"{appId}|{integrationName}|{credentialRef}", out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<McpCredentialRecord>> ListAsync(
            string appId,
            string? integrationName = null,
            CancellationToken cancellationToken = default)
        {
            var rows = _map.Values.Where(r =>
                string.Equals(r.AppId, appId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(integrationName)
                    || string.Equals(r.IntegrationName, integrationName, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult<IReadOnlyList<McpCredentialRecord>>(rows.ToList());
        }

        public Task UpsertAsync(McpCredentialRecord record, CancellationToken cancellationToken = default)
        {
            Put(record);
            return Task.CompletedTask;
        }
    }
}
