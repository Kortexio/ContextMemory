using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class GlobalWikiAliasLexiconTests
{
    [Fact]
    public void Glossary_ExpandsAcronymAndPhrase_Bidirectionally()
    {
        var lexicon = new GlobalWikiAliasLexicon();
        lexicon.ParseGlossary(
            """
            # Glossary
            - RJ: Rio de Janeiro
            - VVE: Virtual Vehicle Enablement
            """);

        Assert.Equal(2, lexicon.PairCount);

        var vve = lexicon.Expand("VVE");
        Assert.Contains(vve.Groups, g =>
            g.Acronym == "vve" && g.ExpansionPhrase == "virtual vehicle enablement");

        var rio = lexicon.Expand("Rio de Janeiro");
        Assert.Contains(rio.Groups, g =>
            g.Acronym == "rj" && g.ExpansionPhrase == "rio de janeiro");

        var ts = GlobalWikiAliasLexicon.ToPostgresTsQuery(vve);
        Assert.NotNull(ts);
        Assert.Contains("vve", ts, StringComparison.Ordinal);
        Assert.Contains("virtual", ts, StringComparison.Ordinal);
        Assert.Contains("&", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseGlossarySections_PreservesManualOverrides()
    {
        var markdown = GlobalWikiAliasLexicon.BuildGlossaryMarkdown(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["vve"] = "virtual vehicle enablement" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["rj"] = "rio de janeiro" });

        var (manual, auto) = GlobalWikiAliasLexicon.ParseGlossarySections(markdown);
        Assert.Equal("rio de janeiro", manual["rj"]);
        Assert.Equal("virtual vehicle enablement", auto["vve"]);
    }

    [Fact]
    public void Harvest_ReadsAliasesLine()
    {
        var lexicon = GlobalWikiAliasLexicon.FromHarvest(
            glossaryMarkdown: null,
            [
                ("Fleet", """
                    Keywords: VVE, fleet
                    Aliases: VVE=Virtual Vehicle Enablement
                    Rule: enable before SOP.
                    """)
            ]);

        Assert.Contains(lexicon.Expand("VVE").Groups, g => g.ExpansionPhrase == "virtual vehicle enablement");
    }

    [Fact]
    public void BuildGlossaryMarkdown_FormatsEntries()
    {
        var md = GlobalWikiAliasLexicon.BuildGlossaryMarkdown(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["vve"] = "virtual vehicle enablement" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("## Auto", md);
        Assert.Contains("VVE: Virtual Vehicle Enablement", md);
    }

    [Fact]
    public void Harvest_ReadsParentheticalAndEmDashPairs()
    {
        var lexicon = GlobalWikiAliasLexicon.FromHarvest(
            glossaryMarkdown: null,
            [
                ("VVE — Virtual Vehicle Enablement", "Keywords: VVE (Virtual Vehicle Enablement), fleet")
            ]);

        var expansion = lexicon.Expand("vve");
        Assert.Contains(expansion.Groups, g => g.ExpansionPhrase == "virtual vehicle enablement");
    }

    [Fact]
    public void ScoreMatches_FindsExpansionWhenQueryIsAcronym()
    {
        var lexicon = new GlobalWikiAliasLexicon();
        lexicon.ParseGlossary("- VVE: Virtual Vehicle Enablement");

        var hit = Doc("vve-doc", "Fleet spec", "Virtual Vehicle Enablement is required for EU markets.");
        var decoy = Doc("other", "Parking", "Vehicle parking policy for the office.");

        var ranked = GlobalWikiScoring.ScoreMatches([decoy, hit], "VVE", lexicon).Select(x => x.Document.DocumentId).ToList();

        Assert.Equal("vve-doc", ranked[0]);
        Assert.Contains("vve-doc", ranked);
    }

    [Fact]
    public void ScoreMatches_FindsAcronymWhenQueryIsExpansion()
    {
        var lexicon = new GlobalWikiAliasLexicon();
        lexicon.ParseGlossary("- RJ: Rio de Janeiro");

        var hit = Doc("rj-site", "Plant RJ", "The RJ plant handles south-east logistics.");
        var decoy = Doc("other", "Lisbon", "Lisbon office hours.");

        var ranked = GlobalWikiScoring.ScoreMatches([decoy, hit], "Rio de Janeiro", lexicon)
            .Select(x => x.Document.DocumentId)
            .ToList();

        Assert.Equal("rj-site", ranked[0]);
    }

    [Fact]
    public void ScoreMatches_DoesNotTreatLooseWordAsAcronymHit()
    {
        var lexicon = new GlobalWikiAliasLexicon();
        lexicon.ParseGlossary("- VVE: Virtual Vehicle Enablement");

        var decoy = Doc("parking", "Parking", "Vehicle parking only.");
        var ranked = GlobalWikiScoring.ScoreMatches([decoy], "VVE", lexicon).ToList();

        Assert.DoesNotContain(ranked, x => x.Score >= 100);
    }

    private static GlobalWikiDocument Doc(string id, string title, string content) =>
        new()
        {
            AppId = "demo",
            DocumentId = id,
            Slug = id,
            Title = title,
            Content = content,
            Summary = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
