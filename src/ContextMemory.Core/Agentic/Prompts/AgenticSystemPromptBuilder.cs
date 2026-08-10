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

        var profile = AgenticPromptProfileResolver.Resolve(runtimeConfig);

        var mcpServers = runtimeConfig.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var mcpLine = mcpServers.Count > 0
            ? $"\nMCP servers: {string.Join(", ", mcpServers)} (use tool_describe before calling unfamiliar MCP tools)."
            : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Agentic mode");
        sb.AppendLine($"Harness profile: {profile}.");
        sb.AppendLine($"Available tools: {toolNamesSummary}.{mcpLine}");
        sb.AppendLine(AgenticPromptProfileResolver.ToolCallingHint(profile));
        sb.AppendLine();
        sb.AppendLine(
            "Dynamic context discovery: long tool outputs are stored as artifacts — "
            + "use artifact_tail/artifact_read with artifactId from observations. "
            + "Call tool_describe before the first invocation of any unfamiliar tool (MCP or built-in). "
            + "Skills: use skill_search then skill_read. "
            + "Requestable rules: rule_search / rule_read. "
            + "Heavy research: delegate_task (depth 1). "
            + "After tool results, answer in the user's language.");

        var skills = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => AgenticSkillActivation.IsSkill(s.Activation))
            .ToList();
        var defaults = skills.Where(s => s.IsDefaultEnabled).Take(3).ToList();
        if (defaults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Default skills (ids — skill_search / skill_read for more)");
            foreach (var skill in defaults)
                sb.AppendLine($"- `{skill.Id}`: {skill.Name}");
        }
        else if (skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Skills available via skill_search / skill_read (not inlined).");
        }

        var alwaysOn = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => AgenticSkillActivation.IsAlwaysOn(s.Activation) && s.IsDefaultEnabled)
            .OrderBy(s => s.SortOrder)
            .ToList();
        if (alwaysOn.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Always-on rules");
            foreach (var rule in alwaysOn)
            {
                sb.AppendLine($"### {rule.Name} (`{rule.Id}`)");
                var body = string.IsNullOrWhiteSpace(rule.PromptMarkdown)
                    ? rule.Description
                    : rule.PromptMarkdown.Trim();
                if (body.Length > 1200)
                    body = body[..1200] + "…";
                sb.AppendLine(body);
            }
        }

        var requestable = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Count(s => AgenticSkillActivation.IsRequestable(s.Activation) && s.IsDefaultEnabled);
        if (requestable > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Requestable rules: {requestable} available via rule_search / rule_read.");
        }

        return sb.ToString().TrimEnd();
    }
}
