using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class GlobalWikiDigestSearchTests
{
    [Fact]
    public void ScoreMatches_QuestionOverlapBoostsDigestMatch()
    {
        var doc = new GlobalWikiDocument
        {
            AppId = "demo",
            DocumentId = "fleet-eu",
            Slug = "fleet-eu",
            Title = "EU fleet",
            Summary = """
                Keywords: fleet, EU
                Questions: when to enable virtual vehicle? | fleet rollout deadline EU
                VVE required before SOP.
                """,
            Content = "SECRET_BODY_ONLY_TOKEN",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var ranked = GlobalWikiScoring.ScoreMatches([doc], "virtual vehicle enable EU", digestOnly: true)
            .Select(x => x.Document.DocumentId)
            .ToList();

        Assert.Equal("fleet-eu", ranked[0]);
        Assert.True(GlobalWikiScoring.ScoreDocument(doc, GlobalWikiAliasLexicon.Empty.Expand("virtual vehicle"), digestOnly: true) > 0);
    }

    [Fact]
    public void DigestOnly_IgnoresBodyOnlyTerms()
    {
        const string secret = "SECRET_BODY_ONLY_TOKEN_XYZ";
        var doc = new GlobalWikiDocument
        {
            AppId = "demo",
            DocumentId = "secret-doc",
            Slug = "secret-doc",
            Title = "Title",
            Summary = "Keywords: public, fleet",
            Content = secret,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var ranked = GlobalWikiScoring.ScoreMatches([doc], secret, digestOnly: true).ToList();
        Assert.Empty(ranked);
    }

    [Fact]
    public void ToPostgresOrTsQuery_JoinsTokensWithOr()
    {
        var expansion = GlobalWikiAliasLexicon.Empty.Expand("virtual vehicle enablement");
        var orQuery = GlobalWikiAliasLexicon.ToPostgresOrTsQuery(expansion);
        Assert.NotNull(orQuery);
        Assert.Contains("|", orQuery);
    }

    [Fact]
    public void ExtractQuestionPhrases_ParsesPipeSeparatedList()
    {
        var phrases = GlobalWikiDigestFields.ExtractQuestionPhrases(
            "Keywords: fleet\nQuestions: when to enable VVE? | EU rollout deadline");
        Assert.Equal(2, phrases.Count);
    }
}
