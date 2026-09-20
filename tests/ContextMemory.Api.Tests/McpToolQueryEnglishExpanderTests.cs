using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;
using ContextMemory.Infrastructure.Agentic.Mcp;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class McpToolQueryEnglishExpanderTests
{
    [Fact]
    public void Expand_PortugueseSubscriptionQuery_AddsEnglishTokens()
    {
        var expanded = McpToolQueryEnglishExpander.Expand(
            "quais as regras de criação de uma subscrição na paccar?");

        Assert.Contains("subscrição", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subscription", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rules", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paccar", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expand_EnglishQuery_UnchangedWhenNoMapHits()
    {
        var q = "list open invoices for account A-001";
        // "account" and "invoice" are already English; map still appends synonyms for matching keys
        var expanded = McpToolQueryEnglishExpander.Expand(q);
        Assert.Contains("account", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invoice", expanded, StringComparison.OrdinalIgnoreCase);
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
                Tools = new AgenticToolsConfig { MaxMcpToolsPerTurn = 5 }
            }
        };
        var tools = new[]
        {
            new McpToolDefinition
            {
                ServerName = "zuora-developer-mcp-PACCAR-ACCP",
                Name = "manage_analytics",
                Description = "Analytics dashboards"
            },
            new McpToolDefinition
            {
                ServerName = "zuora-developer-mcp-PACCAR-ACCP",
                Name = "create_subscriptions",
                Description = "Enhanced subscription creation tool"
            },
            new McpToolDefinition
            {
                ServerName = "zuora-developer-mcp-PACCAR-ACCP",
                Name = "ask_zuora",
                Description = "Expert on Zuora product suite including subscriptions"
            },
            new McpToolDefinition
            {
                ServerName = "zuora-developer-mcp-PACCAR-ACCP",
                Name = "query_objects",
                Description = "Query any Zuora object including subscription"
            },
            new McpToolDefinition
            {
                ServerName = "zuora-developer-mcp-PACCAR-ACCP",
                Name = "manage_journal_runs",
                Description = "Journal runs"
            },
            new McpToolDefinition
            {
                ServerName = "other",
                Name = "unrelated_tool",
                Description = "Something else entirely"
            }
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
