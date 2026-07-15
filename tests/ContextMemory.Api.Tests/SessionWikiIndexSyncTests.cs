using ContextMemory.Core.Session;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SessionWikiIndexSyncTests
{
    [Fact]
    public void TryNormalize_CollapsesNestedPagesPrefix()
    {
        Assert.True(SessionWikiPagePaths.TryNormalize("pages/pages/foo.md", out var path));
        Assert.Equal("pages/foo.md", path);
    }

    [Fact]
    public void Reconcile_RebuildsIndexWhenLinksPointToMissingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-index-sync-" + Guid.NewGuid().ToString("N"));
        var pagesDir = Path.Combine(dir, "pages");
        Directory.CreateDirectory(pagesDir);
        File.WriteAllText(Path.Combine(pagesDir, "relativity_theory.md"), "# Teoria da Relatividade\nConteúdo.");

        try
        {
            const string brokenIndex = """
                # Índice
                - [pages/theory_of_big_bang.md](./pages/theory_of_big_bang.md) - Big Bang
                """;

            var reconciled = SessionWikiIndexSync.Reconcile(dir, brokenIndex);

            Assert.Contains("relativity_theory.md", reconciled, StringComparison.Ordinal);
            Assert.DoesNotContain("theory_of_big_bang", reconciled, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reconcile_RebuildsBulletListIndexWhenPageMissingOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-index-sync-" + Guid.NewGuid().ToString("N"));
        var pagesDir = Path.Combine(dir, "pages");
        Directory.CreateDirectory(pagesDir);
        File.WriteAllText(Path.Combine(pagesDir, "movimento_negro.md"), "# Movimento Negro\nConteúdo.");
        File.WriteAllText(Path.Combine(pagesDir, "movimento_feminista.md"), "# Movimento Feminista\nConteúdo.");

        try
        {
            const string brokenIndex = """
                # Session index
                ### Páginas disponíveis:
                *   **movimento_negro** — Resumo do movimento negro.
                *   **movimento_feminista** — Resumo do movimento feminista.
                *   **movimento_lgbtqia** — Página que o LLM referenciou mas não criou.
                """;

            var reconciled = SessionWikiIndexSync.Reconcile(dir, brokenIndex);

            Assert.Contains("movimento_negro.md", reconciled, StringComparison.Ordinal);
            Assert.Contains("movimento_feminista.md", reconciled, StringComparison.Ordinal);
            Assert.DoesNotContain("movimento_lgbtqia", reconciled, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reconcile_RebuildsIndexWhenNoPageLinksButPagesExistOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-index-sync-" + Guid.NewGuid().ToString("N"));
        var pagesDir = Path.Combine(dir, "pages");
        Directory.CreateDirectory(pagesDir);
        File.WriteAllText(Path.Combine(pagesDir, "foo.md"), "# Foo\nConteúdo.");

        try
        {
            const string proseIndex = """
                # Session index
                * **foo** — Descrição sem link markdown.
                """;

            var reconciled = SessionWikiIndexSync.Reconcile(dir, proseIndex);

            Assert.Contains("[foo](pages/foo.md)", reconciled, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RepairSessionLayout_HoistsNestedPagesAndRebuildsIndex()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-index-sync-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(dir, "pages", "pages");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "leis_de_newton.md"), "# Leis de Newton\nConteúdo.");
        File.WriteAllText(Path.Combine(dir, "index.md"), "# Índice\n- [pages/leis_de_newton.md](pages/leis_de_newton.md)");

        try
        {
            SessionWikiIndexSync.RepairSessionLayout(dir);

            Assert.True(File.Exists(Path.Combine(dir, "pages", "leis_de_newton.md")));
            Assert.False(Directory.Exists(nested));
            var index = File.ReadAllText(Path.Combine(dir, "index.md"));
            Assert.Contains("leis_de_newton.md", index, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
