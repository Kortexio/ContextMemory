using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticSystemPromptBuilder
{
    public static string Build(
        AppRuntimeConfig runtimeConfig,
        string toolNamesSummary)
    {
        if (string.IsNullOrWhiteSpace(toolNamesSummary))
            return string.Empty;

        var mcpServers = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var mcpLine = mcpServers.Count > 0
            ? $"\nMCP servers: {string.Join(", ", mcpServers)}."
            : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Agentic mode");
        sb.AppendLine($"Available tools: {toolNamesSummary}.{mcpLine}");
        sb.AppendLine();
        sb.AppendLine("When invoking a tool, emit valid tool/function call JSON for this backend. After tool results, answer in the user's language.");

        var skills = runtimeConfig.ResolvedPolicy.ActiveSkills;
        if (skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Active skills");
            foreach (var skill in skills)
            {
                if (string.IsNullOrWhiteSpace(skill.PromptMarkdown))
                    continue;
                sb.AppendLine();
                sb.AppendLine(skill.PromptMarkdown.Trim());
            }
        }

        return sb.ToString().TrimEnd();
    }
}
