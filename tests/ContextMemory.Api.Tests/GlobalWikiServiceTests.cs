using ContextMemory.Core.Configuration;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Wiki;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class GlobalWikiServiceTests
{
    [Fact]
    public async Task Upsert_IsIdempotent_WhenContentUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileGlobalWikiStore(Options.Create(new ContextMemoryOptions
            {
                ContentRootPath = root,
                DataPath = "."
            }));
            var service = new GlobalWikiService(store);

            var first = await service.UpsertAsync("demo", "jira:PROJ-1", new GlobalWikiUpsertRequest
            {
                Title = "PROJ-1",
                Content = "# PROJ-1\n\nHello wiki",
                SourceId = "jira:PROJ"
            });

            var second = await service.UpsertAsync("demo", "jira:PROJ-1", new GlobalWikiUpsertRequest
            {
                Title = "PROJ-1",
                Content = "# PROJ-1\n\nHello wiki",
                SourceId = "jira:PROJ"
            });

            Assert.True(first.Created);
            Assert.False(first.Unchanged);
            Assert.True(second.Unchanged);
            Assert.Equal(first.ContentHash, second.ContentHash);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Query_ReturnsMatchingDocuments_AndIsolatesApps()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileGlobalWikiStore(Options.Create(new ContextMemoryOptions
            {
                ContentRootPath = root,
                DataPath = "."
            }));
            var service = new GlobalWikiService(store);

            await service.UpsertAsync("app-a", "doc-1", new GlobalWikiUpsertRequest
            {
                Content = "# Renewal\n\nSubscription renewal policy details",
                SourceId = "confluence:DOCS"
            });
            await service.UpsertAsync("app-b", "doc-1", new GlobalWikiUpsertRequest
            {
                Content = "# Unrelated\n\nOther tenant secret",
                SourceId = "confluence:DOCS"
            });

            var result = await service.QueryAsync("app-a", new GlobalWikiQueryRequest
            {
                Query = "subscription renewal",
                TopK = 5,
                BudgetChars = 4000,
                IncludeIndex = false
            });

            Assert.Equal(1, result.TotalDocuments);
            Assert.Contains(result.Matches, m => m.DocumentId == "doc-1");
            Assert.DoesNotContain(result.CompiledMarkdown, "Other tenant secret");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
