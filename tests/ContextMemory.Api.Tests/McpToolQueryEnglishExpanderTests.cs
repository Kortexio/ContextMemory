using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic.Mcp;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class McpToolQueryEnglishExpanderTests
{
    private static readonly Dictionary<string, string> TestLexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        ["subscrição"] = "subscription",
        ["subscricao"] = "subscription",
        ["criação de subscrição"] = "subscription creation create",
        ["criacao de subscricao"] = "subscription creation create",
        ["regras de criação"] = "creation rules",
        ["regras de criacao"] = "creation rules",
        ["criação"] = "create creation",
        ["criacao"] = "create creation",
        ["criar"] = "create",
        ["regras"] = "rules",
        ["paccar"] = "paccar",
        ["account"] = "account",
        ["invoice"] = "invoice"
    };

    [Fact]
    public void Expand_PortugueseSubscriptionQuery_AddsEnglishTokens()
    {
        var expanded = McpArgumentShaping.ExpandQuery(
            "quais as regras de criação de uma subscrição na paccar?",
            TestLexicon);

        Assert.Contains("subscrição", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subscription", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rules", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paccar", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expand_EmptyLexicon_ReturnsOriginal()
    {
        var q = "quais as regras de criação de uma subscrição";
        var expanded = McpArgumentShaping.ExpandQuery(q, null);
        Assert.Equal(q, expanded);
    }

    [Fact]
    public void Selector_PortugueseSubscriptionQuery_PrefersSubscriptionTools()
    {
        var selector = new McpToolSelector();
        var config = new AppRuntimeConfig
        {
            AppId = "demo",
            Agentic = new AgenticConfig
            {
                Tools = new AgenticToolsConfig
                {
                    MaxMcpToolsPerTurn = 5,
                    QueryLexicon = TestLexicon
                }
            }
        };
        var tools = new[]
        {
            new McpToolDefinition { ServerName = "zuora", Name = "manage_analytics", Description = "Analytics dashboards" },
            new McpToolDefinition { ServerName = "zuora", Name = "create_subscriptions", Description = "Enhanced subscription creation tool" },
            new McpToolDefinition { ServerName = "zuora", Name = "ask_zuora", Description = "Expert on Zuora including subscriptions" },
            new McpToolDefinition { ServerName = "zuora", Name = "query_objects", Description = "Query any Zuora object including subscription" },
            new McpToolDefinition { ServerName = "zuora", Name = "manage_journal_runs", Description = "Journal runs" },
            new McpToolDefinition { ServerName = "other", Name = "unrelated_tool", Description = "Something else entirely" }
        };

        var selected = selector.SelectTools(
            config,
            tools,
            "quais as regras de criação de uma subscrição na paccar?");

        Assert.Contains(selected, t => t.Name == "create_subscriptions");
        Assert.Contains(selected, t => t.Name is "ask_zuora" or "query_objects");
        Assert.DoesNotContain(selected, t => t.Name == "unrelated_tool");
    }
}
