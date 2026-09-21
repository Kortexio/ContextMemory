using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Infrastructure.Agentic;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SessionDiscoveryToolExecutorTests
{
    [Fact]
    public void ToolArguments_AcceptCamelSnakeKebabAndCaseVariants()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            """{"artifact_id":"a","max-chars":42,"PERSIST_TO_WIKI":true}""");

        Assert.Equal("a", AgenticToolArguments.GetString(doc.RootElement, "artifactId"));
        Assert.Equal(42, AgenticToolArguments.GetInt(doc.RootElement, "maxChars", 0));
        Assert.True(AgenticToolArguments.GetBool(doc.RootElement, "persistToWiki"));
    }

    [Fact]
    public async Task ArtifactRead_AcceptsSnakeCaseArtifactId()
    {
        var artifacts = new StubArtifactStore();
        await artifacts.WriteAsync("app", "user", "session", "tool:wiki_search:abc", "full evidence");
        var executor = new SessionDiscoveryToolExecutor(
            artifacts,
            new StubSessionStore(),
            new StubMcpCatalog());

        var result = await executor.ExecuteAsync(
            new OllamaToolCall(new OllamaFunctionCall(
                SessionDiscoveryTools.ArtifactRead,
                """{"artifact_id":"tool:wiki_search:abc"}""")),
            "app",
            "user",
            "session",
            new AppRuntimeConfig { AppId = "app" });

        Assert.True(result.Success);
        Assert.Equal("full evidence", result.Output);
    }

    [Fact]
    public async Task ArtifactTail_AcceptsSnakeCaseArguments()
    {
        var artifacts = new StubArtifactStore();
        await artifacts.WriteAsync("app", "user", "session", "tool:wiki_search:abc", "0123456789");
        var executor = new SessionDiscoveryToolExecutor(
            artifacts,
            new StubSessionStore(),
            new StubMcpCatalog());

        var result = await executor.ExecuteAsync(
            new OllamaToolCall(new OllamaFunctionCall(
                SessionDiscoveryTools.ArtifactTail,
                """{"artifact_id":"tool:wiki_search:abc","max_chars":4}""")),
            "app",
            "user",
            "session",
            new AppRuntimeConfig { AppId = "app" });

        Assert.True(result.Success);
        Assert.Equal("6789", result.Output);
    }

    [Fact]
    public async Task DiscoveryTool_InvalidJson_ReturnsFailureInsteadOfThrowing()
    {
        var executor = new SessionDiscoveryToolExecutor(
            new StubArtifactStore(),
            new StubSessionStore(),
            new StubMcpCatalog());

        var result = await executor.ExecuteAsync(
            new OllamaToolCall(new OllamaFunctionCall(
                SessionDiscoveryTools.ArtifactRead,
                """{"artifact_id":""")),
            "app",
            "user",
            "session",
            new AppRuntimeConfig { AppId = "app" });

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubArtifactStore : ISessionArtifactStore
    {
        private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

        public Task WriteAsync(
            string appId,
            string userId,
            string sessionId,
            string artifactId,
            string content,
            CancellationToken cancellationToken = default)
        {
            _items[artifactId] = content;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(
            string appId,
            string userId,
            string sessionId,
            string artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.GetValueOrDefault(artifactId));

        public Task<string?> TailAsync(
            string appId,
            string userId,
            string sessionId,
            string artifactId,
            int maxChars,
            CancellationToken cancellationToken = default)
        {
            var content = _items.GetValueOrDefault(artifactId);
            return Task.FromResult(content is null
                ? null
                : content[^Math.Min(maxChars, content.Length)..]);
        }
    }

    private sealed class StubSessionStore : ISessionStore
    {
        public Task<SessionSnapshot> LoadAsync(
            string appId,
            string userId,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SessionSnapshot { SessionPath = sessionId });

        public Task EnsureInitializedAsync(
            string appId,
            string userId,
            string sessionId,
            string appSchema,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendMessagesAsync(
            string appId,
            string userId,
            string sessionId,
            IEnumerable<OllamaMessage> messages,
            int maxMessages,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyWikiUpdateAsync(
            string appId,
            string userId,
            string sessionId,
            SessionWikiUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> DeleteSessionsOlderThanAsync(
            string appId,
            DateTimeOffset olderThan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> DeleteSessionsForUserAsync(
            string appId,
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class StubMcpCatalog : IMcpToolCatalog
    {
        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
            AppRuntimeConfig runtimeConfig,
            string? userQuery = null,
            IReadOnlyList<string>? recentToolNames = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public Task<IReadOnlyList<McpToolDefinition>> GetAllToolsAsync(
            AppRuntimeConfig runtimeConfig,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public void Invalidate(string appId)
        {
        }

        public Task<IReadOnlyList<McpCatalogSyncResult>> SyncAsync(
            AppRuntimeConfig runtimeConfig,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpCatalogSyncResult>>([]);
    }
}
