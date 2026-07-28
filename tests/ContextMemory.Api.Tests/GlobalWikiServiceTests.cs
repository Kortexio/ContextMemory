using ContextMemory.Core.Configuration;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Wiki;
using Microsoft.Extensions.Logging.Abstractions;
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
            var digest = new StubDigestGenerator();
            var service = CreateService(root, digest);

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
            Assert.Equal(1, digest.Calls);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Upsert_StoresLlmDigest_AndRefreshesCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var digest = new StubDigestGenerator
            {
                Digest =
                    """
                    Keywords: PAC-668, Zuora, billing, reconciliation
                    Payment reconciliation blocked on Zuora.
                    Rule from comment: never reopen closed invoice batches.
                    """
            };
            var store = CreateStore(root);
            var service = new GlobalWikiService(store, digest, NullLogger<GlobalWikiService>.Instance);

            await service.UpsertAsync("demo", "PAC-668", new GlobalWikiUpsertRequest
            {
                Title = "PAC-668",
                Content = "# PAC-668\n\nTicket body\n\nComment: never reopen closed invoice batches.",
                SourceId = "jira:PAC"
            });

            var stored = await store.GetAsync("demo", "PAC-668");
            Assert.NotNull(stored);
            Assert.Contains("Keywords: PAC-668", stored!.Summary);
            Assert.Contains("never reopen closed invoice batches", stored.Summary);

            var catalog = await store.GetAsync("demo", GlobalWikiCatalog.DocumentId);
            Assert.NotNull(catalog);
            Assert.Contains("PAC-668", catalog!.Content);
            Assert.Contains("Keywords: PAC-668", catalog.Content);
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
            var service = CreateService(root);

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

            Assert.True(result.TotalDocuments >= 1);
            Assert.Contains(result.Matches, m => m.DocumentId == "doc-1");
            Assert.DoesNotContain(result.CompiledMarkdown, "Other tenant secret");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Query_PacksMatchedDocumentBody_BeforeFillerIndexPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService(root);

            // Simulate a large Jira corpus: many short PAC-N pages that would fill a naive packer.
            for (var i = 1; i <= 80; i++)
            {
                await service.UpsertAsync("companybrain", $"PAC-{i}", new GlobalWikiUpsertRequest
                {
                    Title = $"PAC-{i}",
                    Content = $"# PAC-{i}\n\nShort filler ticket {i}.",
                    SourceId = "jira:PAC",
                    Summary = $"Filler {i}"
                });
            }

            const string targetBody = "UNIQUE_PAC_668_BODY_MARKER: payment reconciliation blocked on Zuora.";
            await service.UpsertAsync("companybrain", "PAC-668", new GlobalWikiUpsertRequest
            {
                Title = "PAC-668",
                Content = $"# PAC-668\n\n{targetBody}\n\nMore detail about the billing incident.",
                SourceId = "jira:PAC",
                Summary = "Billing reconciliation"
            });

            // Tight budget: old behaviour filled with PAC-1.. index/pages and dropped PAC-668.
            var result = await service.QueryAsync("companybrain", new GlobalWikiQueryRequest
            {
                Query = "PAC-668",
                TopK = 5,
                BudgetChars = 2_000,
                IncludeIndex = true
            });

            Assert.Contains(result.Matches, m => m.DocumentId == "PAC-668");
            Assert.Equal("PAC-668", result.Matches[0].DocumentId);
            Assert.Contains(targetBody, result.CompiledMarkdown);
            Assert.DoesNotContain("Short filler ticket 1.", result.CompiledMarkdown);

            var bodyPos = result.CompiledMarkdown.IndexOf(targetBody, StringComparison.Ordinal);
            var indexPos = result.CompiledMarkdown.IndexOf("## Index", StringComparison.Ordinal);
            Assert.True(indexPos < 0 || bodyPos < indexPos,
                "Matched document body must appear before any optional index.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalizeDigest_EnforcesKeywordsLine_AndMaxSixLines()
    {
        var raw =
            """
            Payment issue on Zuora
            Rule: never reopen closed batches
            Extra 1
            Extra 2
            Extra 3
            Extra 4
            Extra 5 should drop
            """;

        var normalized = GlobalWikiDigestGenerator.NormalizeDigest(raw, "PAC-668", "Billing");
        var lines = normalized.Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("Keywords:", lines[0]);
        Assert.Contains("never reopen closed batches", normalized);
        Assert.DoesNotContain("Extra 5 should drop", normalized);
    }

    private static GlobalWikiService CreateService(string root, StubDigestGenerator? digest = null) =>
        new(CreateStore(root), digest ?? new StubDigestGenerator(), NullLogger<GlobalWikiService>.Instance);

    private static FileGlobalWikiStore CreateStore(string root) =>
        new(Options.Create(new ContextMemoryOptions
        {
            ContentRootPath = root,
            DataPath = "."
        }));

    private sealed class StubDigestGenerator : IGlobalWikiDigestGenerator
    {
        public int Calls { get; private set; }
        public string? Digest { get; init; }

        public Task<string> GenerateAsync(
            string appId,
            string documentId,
            string? title,
            string? sourceId,
            string content,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!string.IsNullOrWhiteSpace(Digest))
                return Task.FromResult(Digest);

            return Task.FromResult(
                $"Keywords: {documentId}\nAuto digest for {title ?? documentId}.");
        }
    }
}
