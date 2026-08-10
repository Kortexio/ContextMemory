using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticToolIntentNarrationGuardrailTests
{
    [Fact]
    public void Rejects_PortugueseNarration_WithoutTools()
    {
        var config = AgenticConfig();
        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "Vou buscar o conteúdo completo dos tickets PAC-759, PAC-762 e PAC-769 usando a ferramenta wiki_search para localizar os documentos.",
            [],
            config,
            out var feedback);

        Assert.True(ok);
        Assert.Contains("tool_calls", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_PermissionAsk_WithoutTools()
    {
        var config = AgenticConfig();
        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "Posso usar as tools wiki_search para procurar esses tickets?",
            [],
            config,
            out _);

        Assert.True(ok);
    }

    [Fact]
    public void Accepts_AfterSuccessfulTool()
    {
        var config = AgenticConfig();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"PAC-759"}""",
                Output = "found",
                Success = true
            }
        };

        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "PAC-759 is about billing reconciliation.",
            steps,
            config,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Rejects_ToolNameLeak_EvenAfterSuccessfulTool()
    {
        var config = AgenticConfig();
        var steps = new List<AgentExecutionStep>
        {
            new()
            {
                Iteration = 1,
                ToolName = "wiki_search",
                Arguments = """{"query":"PAC-759"}""",
                Output = "found",
                Success = true
            }
        };

        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "Via wiki_search, PAC-759 covers billing reconciliation.",
            steps,
            config,
            out var feedback);

        Assert.True(ok);
        Assert.Contains("tool", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ignores_NormalAnswer_WithoutToolMentions()
    {
        var config = AgenticConfig();
        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "Lisbon is the capital of Portugal.",
            [],
            config,
            out _);

        Assert.False(ok);
    }

    private static AppRuntimeConfig AgenticConfig() =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "pt",
            Agentic = new AgenticConfig { Enabled = true }
        };
}
