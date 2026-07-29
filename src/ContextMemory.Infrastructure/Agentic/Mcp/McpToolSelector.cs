using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

public sealed class McpToolSelector : IMcpToolSelector
{
    public IReadOnlyList<McpToolDefinition> SelectTools(
        AppRuntimeConfig runtimeConfig,
        IReadOnlyList<McpToolDefinition> tools,
        string? userQuery,
        IReadOnlyList<string>? recentToolNames = null)
    {
        if (tools.Count == 0)
            return tools;

        var recent = recentToolNames?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var query = userQuery ?? string.Empty;
        var tokens = query.Split([' ', '\t', '\n', '\r', ':', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var max = runtimeConfig.Agentic.Tools.MaxMcpToolsPerTurn > 0
            ? runtimeConfig.Agentic.Tools.MaxMcpToolsPerTurn
            : 12;

        return tools
            .Select(t => (Tool: t, Score: Score(t, tokens, recent)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Tool.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Tool.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Tool)
            .ToList();
    }

    private static int Score(
        McpToolDefinition tool,
        HashSet<string> tokens,
        HashSet<string> recentToolNames)
    {
        var score = 0;
        var haystack = $"{tool.ServerName} {tool.Name} {tool.QualifiedName} {tool.Description}".ToLowerInvariant();

        foreach (var token in tokens)
        {
            if (tool.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 30;
            else if (tool.ServerName.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 20;
            else if (haystack.Contains(token, StringComparison.Ordinal))
                score += 10;
        }

        if (recentToolNames.Contains(tool.QualifiedName) || recentToolNames.Contains(tool.Name))
            score += 25;

        if (tool.Name.StartsWith("search", StringComparison.OrdinalIgnoreCase)
            || tool.Name.StartsWith("find", StringComparison.OrdinalIgnoreCase)
            || tool.Name.StartsWith("list", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }
}
