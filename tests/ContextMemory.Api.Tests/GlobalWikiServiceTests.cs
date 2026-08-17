using ContextMemory.Core.Configuration;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
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
            Assert.Equal(0, digest.Calls);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildDigests_RunsAfterIngest_AndRefreshesCatalog()
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

            Assert.Equal(0, digest.Calls);

            var before = await store.GetAsync("demo", "PAC-668");
            Assert.NotNull(before);
            Assert.DoesNotContain("Keywords: PAC-668, Zuora", before!.Summary);

            var rebuild = await service.RebuildDigestsAsync("demo", new GlobalWikiDigestRebuildRequest());
            Assert.Equal(1, rebuild.Updated);
            Assert.Equal(1, digest.Calls);
            Assert.True(rebuild.CatalogRefreshed);

            var stored = await store.GetAsync("demo", "PAC-668");
            Assert.NotNull(stored);
            Assert.Contains("Keywords: PAC-668", stored!.Summary);
            Assert.Contains("never reopen closed invoice batches", stored.Summary);

            var catalog = await store.GetAsync("demo", GlobalWikiCatalog.DocumentId);
            Assert.NotNull(catalog);
            Assert.Contains("PAC-668", catalog!.Content);
            Assert.Contains("Keywords: PAC-668", catalog.Content);

            var second = await service.RebuildDigestsAsync("demo", new GlobalWikiDigestRebuildRequest());
            Assert.Equal(0, second.Updated);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(1, digest.Calls);
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
    public async Task Query_DigestOnly_PacksSummaryNotFullBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService(root);
            const string secretBody = "SECRET_FULL_BODY_SHOULD_NOT_APPEAR";
            await service.UpsertAsync("demo", "doc-digest", new GlobalWikiUpsertRequest
            {
                Title = "Digest doc",
                Content = $"# Digest doc\n\n{secretBody}\n\nLong policy text.",
                Summary = "Keywords: renewal, SLA\nRefunds within 14 days.",
                SourceId = "confluence:DOCS"
            });

            var result = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "renewal SLA",
                TopK = 3,
                BudgetChars = 2_000,
                DigestOnly = true
            });

            Assert.Contains(result.Matches, m => m.DocumentId == "doc-digest");
            Assert.Contains("Keywords: renewal", result.CompiledMarkdown);
            Assert.DoesNotContain(secretBody, result.CompiledMarkdown);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildDigests_AutoRefreshesGlossaryFromSummaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var digest = new StubDigestGenerator
            {
                Digest =
                    """
                    Keywords: VVE, fleet, EU
                    Aliases: VVE=Virtual Vehicle Enablement
                    Virtual Vehicle Enablement is mandatory before SOP.
                    """
            };
            var service = CreateService(root, digest);

            await service.UpsertAsync("demo", "fleet-eu", new GlobalWikiUpsertRequest
            {
                Title = "EU fleet rollout",
                Content = "# EU fleet\n\nVirtual Vehicle Enablement is mandatory before SOP.",
                SourceId = "confluence:FLEET"
            });

            var rebuild = await service.RebuildDigestsAsync("demo", new GlobalWikiDigestRebuildRequest());
            Assert.True(rebuild.GlossaryRefreshed);
            Assert.True(rebuild.GlossaryPairs >= 1);

            var glossary = await CreateStore(root).GetAsync("demo", GlobalWikiCatalog.GlossaryDocumentId);
            Assert.NotNull(glossary);
            Assert.Contains("VVE", glossary!.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Virtual Vehicle Enablement", glossary.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("## Auto", glossary.Content);

            var byAcronym = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "VVE",
                TopK = 5,
                BudgetChars = 4000
            });
            Assert.Contains(byAcronym.Matches, m => m.DocumentId == "fleet-eu");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildDigests_PreservesManualGlossaryOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService(root);
            await service.UpsertAsync("demo", GlobalWikiCatalog.GlossaryDocumentId, new GlobalWikiUpsertRequest
            {
                Title = GlobalWikiCatalog.GlossaryTitle,
                Content = GlobalWikiAliasLexicon.BuildGlossaryMarkdown(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["rj"] = "rio de janeiro"
                    }),
                SourceId = "wiki:glossary",
                Overwrite = true
            });

            var digest = new StubDigestGenerator
            {
                Digest = "Keywords: VVE (Virtual Vehicle Enablement)\nRule line."
            };
            service = new GlobalWikiService(CreateStore(root), digest, NullLogger<GlobalWikiService>.Instance);
            await service.UpsertAsync("demo", "fleet-eu", new GlobalWikiUpsertRequest
            {
                Title = "Fleet",
                Content = "# Fleet\n\nVVE rollout.",
                SourceId = "confluence:FLEET"
            });

            await service.RebuildDigestsAsync("demo", new GlobalWikiDigestRebuildRequest());

            var glossary = await CreateStore(root).GetAsync("demo", GlobalWikiCatalog.GlossaryDocumentId);
            Assert.NotNull(glossary);
            Assert.Contains("RJ: Rio de Janeiro", glossary!.Content);
            Assert.Contains("## Manual overrides", glossary.Content);
            Assert.Contains("VVE", glossary.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Query_ResolvesAcronymsViaGlossary()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService(root);

            await service.UpsertAsync("demo", GlobalWikiCatalog.GlossaryDocumentId, new GlobalWikiUpsertRequest
            {
                Title = GlobalWikiCatalog.GlossaryTitle,
                Content = "- VVE: Virtual Vehicle Enablement\n- RJ: Rio de Janeiro\n",
                SourceId = "wiki:glossary"
            });
            await service.UpsertAsync("demo", "fleet-eu", new GlobalWikiUpsertRequest
            {
                Title = "EU fleet rollout",
                Content = "# EU fleet\n\nVirtual Vehicle Enablement is mandatory before SOP.",
                SourceId = "confluence:FLEET"
            });
            await service.UpsertAsync("demo", "plant-rj", new GlobalWikiUpsertRequest
            {
                Title = "Plant RJ capacity",
                Content = "# Plant RJ\n\nRJ line 2 is the overflow plant.",
                SourceId = "confluence:PLANTS"
            });

            var byAcronym = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "VVE",
                TopK = 5,
                BudgetChars = 4000
            });
            Assert.Contains(byAcronym.Matches, m => m.DocumentId == "fleet-eu");
            Assert.DoesNotContain(byAcronym.Matches, m => m.DocumentId == GlobalWikiCatalog.GlossaryDocumentId);

            var byPlace = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "Rio de Janeiro",
                TopK = 5,
                BudgetChars = 4000
            });
            Assert.Contains(byPlace.Matches, m => m.DocumentId == "plant-rj");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Query_DigestOnly_DoesNotMatchBodyOnlyTerms()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService(root);
            const string secretBody = "SECRET_BODY_ONLY_DIGEST_ISOLATION";
            await service.UpsertAsync("demo", "body-only", new GlobalWikiUpsertRequest
            {
                Title = "Body only",
                Content = $"# Body\n\n{secretBody}",
                Summary = "Keywords: public, fleet\nQuestions: fleet policy overview?",
                SourceId = "confluence:DOCS"
            });

            var result = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = secretBody,
                TopK = 5,
                BudgetChars = 2000,
                DigestOnly = true
            });

            Assert.DoesNotContain(result.Matches, m => m.DocumentId == "body-only");
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
    public void NormalizeDigest_EnforcesKeywordsLine_AndMaxEightLines()
    {
        var raw =
            """
            Payment issue on Zuora
            Rule: never reopen closed batches
            Extra 1
            Extra 2
            Extra 3
            Extra 4
            Extra 5
            Extra 6
            Extra 7 should drop
            """;

        var normalized = GlobalWikiDigestGenerator.NormalizeDigest(raw, "PAC-668", "Billing");
        var lines = normalized.Split('\n');
        Assert.Equal(8, lines.Length);
        Assert.StartsWith("Keywords:", lines[0]);
        Assert.Contains("never reopen closed batches", normalized);
        Assert.DoesNotContain("Extra 7 should drop", normalized);
    }

    [Fact]
    public async Task Upsert_SupersedesPreviousRevision_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = CreateStore(root);
            var service = new GlobalWikiService(store, new StubDigestGenerator(), NullLogger<GlobalWikiService>.Instance);

            var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            await service.UpsertAsync("demo", "kyc:user-1", new GlobalWikiUpsertRequest
            {
                Title = "KYC",
                Content = "# KYC\n\nStatus: pending",
                ValidFrom = t0
            });

            var mid = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
            var second = await service.UpsertAsync("demo", "kyc:user-1", new GlobalWikiUpsertRequest
            {
                Title = "KYC",
                Content = "# KYC\n\nStatus: approved",
                ValidFrom = mid
            });

            Assert.True(second.Superseded);
            Assert.False(second.Unchanged);

            var active = await store.GetAsync("demo", "kyc:user-1");
            Assert.NotNull(active);
            Assert.Contains("approved", active!.Content);
            Assert.Equal(GlobalWikiRevisionStatus.Active, active.Status);

            var revs = await store.ListRevisionsAsync("demo", "kyc:user-1");
            Assert.Equal(2, revs.Count);
            Assert.Contains(revs, r => r.Status == GlobalWikiRevisionStatus.Superseded);

            var past = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "KYC Status",
                AsOf = DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                TopK = 5
            });
            Assert.Contains("pending", past.CompiledMarkdown);
            Assert.DoesNotContain("approved", past.CompiledMarkdown);

            var now = await service.QueryAsync("demo", new GlobalWikiQueryRequest
            {
                Query = "KYC Status",
                TopK = 5
            });
            Assert.Contains("approved", now.CompiledMarkdown);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Upsert_Overwrite_DoesNotCreateRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-global-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = CreateStore(root);
            var service = new GlobalWikiService(store, new StubDigestGenerator(), NullLogger<GlobalWikiService>.Instance);

            var first = await service.UpsertAsync("demo", "doc-ow", new GlobalWikiUpsertRequest
            {
                Content = "# A\n\nversion one"
            });
            var second = await service.UpsertAsync("demo", "doc-ow", new GlobalWikiUpsertRequest
            {
                Content = "# A\n\nversion two",
                Overwrite = true
            });

            Assert.False(second.Superseded);
            Assert.Equal(first.RevisionId, second.RevisionId);
            var revs = await store.ListRevisionsAsync("demo", "doc-ow");
            Assert.Single(revs);
            Assert.Contains("version two", revs[0].Content);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferActiveFacts_DropsSupersededLines()
    {
        var md =
            """
            ## Facts
            - [superseded] email: old@example.com | valid_from: 2025-01-01 | valid_to: 2026-01-01
            - [active] email: new@example.com | valid_from: 2026-01-01
            Other notes stay.
            """;

        var filtered = SessionWikiCompiler.PreferActiveFacts(md);
        Assert.DoesNotContain("old@example.com", filtered);
        Assert.Contains("new@example.com", filtered);
        Assert.Contains("Other notes stay.", filtered);
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
