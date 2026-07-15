using ContextMemory.Core.Session;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SessionWikiJsonParserTests
{
    [Fact]
    public void TryParseUpdate_ParsesRawJson()
    {
        const string json = """
            {
              "log_entry": "## [2026-05-27 10:00] turno | teste",
              "pages": [{ "path": "pages/a.md", "content": "conteúdo" }]
            }
            """;

        var update = SessionWikiJsonParser.TryParseUpdate(json);

        Assert.NotNull(update);
        Assert.Contains("turno", update!.LogEntry);
        Assert.Single(update.Pages);
    }

    [Fact]
    public void TryParseUpdate_ExtractsJsonFromCodeFence()
    {
        const string raw = """
            Aqui está o JSON:
            ```json
            {"log_entry":"## [2026-05-27 10:00] turno | fenced","pages":[]}
            ```
            """;

        var update = SessionWikiJsonParser.TryParseUpdate(raw);

        Assert.NotNull(update);
        Assert.Contains("fenced", update!.LogEntry);
    }

    [Fact]
    public void TryParseUpdate_ReturnsNullForEmpty()
    {
        Assert.Null(SessionWikiJsonParser.TryParseUpdate(""));
        Assert.Null(SessionWikiJsonParser.TryParseUpdate("not json at all"));
    }

    [Fact]
    public void CreateFallbackLogEntry_AlwaysHasLogLine()
    {
        var update = SessionWikiJsonParser.CreateFallbackLogEntry("erro de teste");

        Assert.NotNull(update.LogEntry);
        Assert.StartsWith("## [", update.LogEntry);
    }
}
