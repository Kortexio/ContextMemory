using ContextMemory.Core.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic;
using ContextMemory.Infrastructure.Wiki;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class GlobalWikiToolExecutorTests
{
    [Fact]
    public void BuildSchemas_EmitsSearchFetchGrep_WhenEnabled()
    {
        var executor = CreateExecutor(Path.Combine(Path.GetTempPath(), "cm-wiki-tool-" + Guid.NewGuid().ToString("N")));
        var schemas = executor.BuildSchemas(new AppRuntimeConfig { AppId = "demo", GlobalWikiEnabled = true })
            .Select(t => t.Function.Name)
            .ToList();

        Assert.Contains(AgenticToolRegistry.WikiSearchToolName, schemas);
        Assert.Contains(AgenticToolRegistry.WikiFetchToolName, schemas);
        Assert.Contains(AgenticToolRegistry.WikiGrepToolName, schemas);
    }

    [Fact]
    public void BuildSchemas_Empty_WhenWikiDisabled()
    {
        var executor = CreateExecutor(Path.Combine(Path.GetTempPath(), "cm-wiki-tool-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(executor.BuildSchemas(new AppRuntimeConfig { AppId = "demo", GlobalWikiEnabled = false }));
    }

    [Fact]
    public async Task Search_ReturnsMapWithoutBody_FetchHydratesContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-wiki-tool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileGlobalWikiStore(Options.Create(new ContextMemoryOptions
            {
                ContentRootPath = root,
                DataPath = "."
            }));
            var service = new GlobalWikiService(store, new StubDigest(), NullLogger<GlobalWikiService>.Instance);
            var executor = new GlobalWikiToolExecutor(service);
            var config = new AppRuntimeConfig { AppId = "demo", GlobalWikiEnabled = true };

            await service.UpsertAsync("demo", "staging-host", new GlobalWikiUpsertRequest
            {
                Title = "Staging database host",
                Content = "The staging database host is postgres-staging-01.",
                Summary = "staging database hostname"
            });

            var search = await executor.ExecuteAsync(
                Call(AgenticToolRegistry.WikiSearchToolName, """{"query":"staging database host"}"""),
                "demo",
                config);

            Assert.Equal(0, search.ExitCode);
            Assert.Contains("staging-host", search.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("postgres-staging-01", search.Output, StringComparison.Ordinal);

            var fetch = await executor.ExecuteAsync(
                Call(AgenticToolRegistry.WikiFetchToolName, """{"id":"staging-host"}"""),
                "demo",
                config);

            Assert.Equal(0, fetch.ExitCode);
            Assert.Contains("postgres-staging-01", fetch.Output, StringComparison.Ordinal);

            var missing = await executor.ExecuteAsync(
                Call(AgenticToolRegistry.WikiFetchToolName, """{"id":"missing-doc"}"""),
                "demo",
                config);
            Assert.Equal(1, missing.ExitCode);
            Assert.Contains("not found", missing.Output, StringComparison.OrdinalIgnoreCase);

            var emptyId = await executor.ExecuteAsync(
                Call(AgenticToolRegistry.WikiFetchToolName, "{}"),
                "demo",
                config);
            Assert.Equal(1, emptyId.ExitCode);
            Assert.Contains("non-empty", emptyId.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static GlobalWikiToolExecutor CreateExecutor(string root)
    {
        var store = new FileGlobalWikiStore(Options.Create(new ContextMemoryOptions
        {
            ContentRootPath = root,
            DataPath = "."
        }));
        var service = new GlobalWikiService(store, new StubDigest(), NullLogger<GlobalWikiService>.Instance);
        return new GlobalWikiToolExecutor(service);
    }

    private static OllamaToolCall Call(string name, string args) =>
        new(new OllamaFunctionCall(name, args));

    private sealed class StubDigest : IGlobalWikiDigestGenerator
    {
        public Task<string> GenerateAsync(
            string appId,
            string documentId,
            string? title,
            string? sourceId,
            string content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"Keywords: {documentId}\nDigest.");
    }
}
