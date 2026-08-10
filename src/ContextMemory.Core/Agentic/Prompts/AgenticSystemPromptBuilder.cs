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

        if (mcpServers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## MCP data access (mandatory)");
            sb.AppendLine(
                "Configured MCP servers give live access to external systems (e.g. Zuora). "
                + "For questions about accounts, subscriptions, invoices, payments, or other live records:");
            sb.AppendLine(
                "- You MUST call the relevant MCP tools in this turn (e.g. `…__query_objects`, `…__zuora_graphql`, "
                + "`…__get_account_summary`, `…__manage_customer_accounts`).");
            sb.AppendLine(
                "- Do NOT answer from imagination, refuse for lack of an ID, or claim tools are unavailable.");
            sb.AppendLine(
                "- If the schema is unclear, call `tool_describe` once, then call the tool with filters "
                + "(example: account status Canceled via `query_objects`).");
            sb.AppendLine(
                "- Prefer MCP over sandbox/python HTTP. If an MCP call fails, report the tool error.");
        }

        var skills = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => AgenticSkillActivation.IsSkill(s.Activation))
            .ToList();
        // Prefer integration / evidence skills in the short id list when MCP is configured.
        var defaults = skills
            .Where(s => s.IsDefaultEnabled)
            .OrderByDescending(s => mcpServers.Count > 0 && IsMcpRelevantSkill(s.Id))
            .ThenBy(s => s.SortOrder)
            .Take(5)
            .ToList();
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

    private static bool IsMcpRelevantSkill(string skillId) =>
        skillId.Contains("mcp", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("zuora", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("evidence", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("ground", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("tool-calling", StringComparison.OrdinalIgnoreCase);
}
