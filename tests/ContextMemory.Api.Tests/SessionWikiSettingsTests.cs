using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Xunit;

namespace ContextMemory.Api.Tests;

public class SessionWikiSettingsTests
{
    [Fact]
    public void ResolveWikiLlmModel_PrefersAppWikiModel()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "a",
            WikiLlmModel = "wiki-small",
            LlmModel = "chat-large"
        };

        Assert.Equal("wiki-small", SessionWikiSettings.ResolveWikiLlmModel(config, "platform-wiki"));
    }

    [Fact]
    public void ResolveWikiLlmModel_UsesPlatformWhenAppEmpty()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "a",
            WikiLlmModel = "  ",
            LlmModel = "chat-large"
        };

        Assert.Equal("platform-wiki", SessionWikiSettings.ResolveWikiLlmModel(config, "platform-wiki"));
    }

    [Fact]
    public void ResolveWikiLlmModel_FallsBackToAppChatModel()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "a",
            WikiLlmModel = "",
            LlmModel = "chat-large"
        };

        Assert.Equal("chat-large", SessionWikiSettings.ResolveWikiLlmModel(config, null));
        Assert.Equal("chat-large", SessionWikiSettings.ResolveWikiLlmModel(config, "  "));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void ResolveWikiUpdateEveryNTurns_TreatsNonPositiveAsOne(int configured, int expected)
    {
        var config = new AppRuntimeConfig { AppId = "a", WikiUpdateEveryNTurns = configured };
        Assert.Equal(expected, SessionWikiSettings.ResolveWikiUpdateEveryNTurns(config));
    }

    [Theory]
    [InlineData(1, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(6, 3, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 3, false)]
    public void ShouldRunWikiLlm_UsesAssistantTurnModulo(int assistantTurns, int everyN, bool expected)
    {
        var messages = new List<OllamaMessage>();
        for (var i = 0; i < assistantTurns; i++)
        {
            messages.Add(new OllamaMessage { Role = "user", Content = $"u{i}" });
            messages.Add(new OllamaMessage { Role = "assistant", Content = $"a{i}" });
        }

        var snapshot = new SessionSnapshot
        {
            SessionPath = "/tmp",
            Messages = messages
        };

        Assert.Equal(expected, SessionWikiSettings.ShouldRunWikiLlm(snapshot, everyN));
    }
}
