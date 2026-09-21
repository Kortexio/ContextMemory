using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticThinkingLeakGuardrailTests
{
    [Fact]
    public void Rejects_MetaReasoningAboutGuardrailFeedback()
    {
        var answer =
            "The user wants me to rewrite the final answer to remove internal tool names, APIs, or mechanics. "
            + "However, I haven't actually generated a final answer yet in this turn.";

        var ok = AgenticThinkingLeakGuardrail.TryGetRejectionFeedback(
            answer,
            ThinkingLeakConfigJson(),
            Config(),
            out var feedback);

        Assert.True(ok);
        Assert.Contains("chain-of-thought", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepts_NormalUserFacingAnswer()
    {
        var ok = AgenticThinkingLeakGuardrail.TryGetRejectionFeedback(
            "Para criar uma subscrição na PACCAR é necessário um VIN válido e um produto ativo.",
            ThinkingLeakConfigJson(),
            Config(),
            out _);

        Assert.False(ok);
    }

    private static string ThinkingLeakConfigJson() =>
        AgenticCatalogSeed.Guardrails.First(g => g.Id == "thinking-leak").ConfigJson;

    private static AppRuntimeConfig Config() =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "en",
            Agentic = new AgenticConfig { Enabled = true }
        };
}
