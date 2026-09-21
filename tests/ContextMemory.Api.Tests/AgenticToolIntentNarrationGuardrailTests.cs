using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticToolIntentNarrationGuardrailTests
{
    [Fact]
    public void Rejects_ToolIntentNarration_WithoutTools()
    {
        var config = AgenticConfig();
        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "I will search the full content of tickets PAC-759, PAC-762 and PAC-769 using the tool wiki_search to locate the documents.",
            [],
            ToolSurfaceConfigJson(),
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
            "Can I use the tools wiki_search to look up those tickets?",
            [],
            ToolSurfaceConfigJson(),
            config,
            out _);

        Assert.True(ok);
    }

    [Fact]
    public void Rejects_McpNarration_WithActionableToolJson()
    {
        var config = AgenticConfig(withMcp: true);
        var ok = AgenticToolIntentNarrationGuardrail.TryGetRejectionFeedback(
            "I will use tool_search to find the account in the billing system.",
            [],
            ToolSurfaceConfigJson(),
            config,
            out var feedback);

        Assert.True(ok);
        Assert.Contains("{\"tool\"", feedback, StringComparison.Ordinal);
        Assert.Contains("exact_name_from_catalog", feedback, StringComparison.OrdinalIgnoreCase);
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
            ToolSurfaceConfigJson(),
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
            ToolSurfaceConfigJson(),
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
            ToolSurfaceConfigJson(),
            config,
            out _);

        Assert.False(ok);
    }

    private static string ToolSurfaceConfigJson() =>
        JsonSerializer.Serialize(new
        {
            kind = AgenticGuardrailKinds.ToolSurfaceHidden,
            feedback =
                "Rejected: you narrated an intent to use tools (or asked permission) instead of calling them. "
                + "Emit tool_calls now with valid JSON — do not ask the user, do not announce.",
            feedbackWithEvidence =
                "Rejected: the final answer names internal tools. "
                + "Answer the user's original question with the result only — no tool names or harness mention.",
            feedbackMcp =
                "Rejected: do not narrate or name tools. Emit a tool call now as ONLY JSON "
                + "{\"tool\":\"exact_name_from_catalog\",\"arguments\":{...}} "
                + "(prefer listed MCP tools for live data; tool_describe if the schema is unclear).",
            toolNameMarkers = new[]
            {
                "wiki_search", "wiki_grep", "wiki_get", "wiki_read", "fetch_url", "http_request", "web_search",
                "query_objects", "python_execute", "shell_execute", "node_execute", "browser_navigate",
                "browser_snapshot", "browser_click", "browser_type", "browser_screenshot", "read_image",
                "parse_pdf", "canvas_write", "canvas_read", "todo_write", "tool_search", "tool_describe",
                "skill_search", "skill_read", "rule_search", "rule_read", "tool_calls", "tool call", "tool_call"
            },
            intentPhrases = new[]
            {
                "i'll use", "i will use", "i am going to use", "i'm going to use",
                "let me use", "may i use", "can i use", "should i use", "going to use", "i'll call",
                "i will call", "i'll search", "i will search", "i'll look up", "using the tool", "using tools",
                "allow me to use", "would you like me to use"
            }
        });

    private static AppRuntimeConfig AgenticConfig(bool withMcp = false) =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "pt",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                Tools = withMcp
                    ? new AgenticToolsConfig
                    {
                        Integrations =
                        [
                            new IntegrationToolConfig
                            {
                                Type = "mcp",
                                Name = "zuora",
                                Enabled = true,
                                Url = "mock://zuora"
                            }
                        ]
                    }
                    : new AgenticToolsConfig()
            }
        };
}
