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
        var capabilities = LlmCapabilitiesResolver.From(runtimeConfig);

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
        sb.AppendLine($"Harness profile: {profile} ({capabilities.HarnessMode}).");
        sb.AppendLine($"Available tools: {toolNamesSummary}.{mcpLine}");
        sb.AppendLine(AgenticPromptProfileResolver.ToolCallingHint(profile));
        sb.AppendLine();
        sb.AppendLine(
            "Dynamic context discovery: long tool outputs are stored as artifacts — "
            + "use artifact_tail/artifact_read with artifactId from observations. "
            + "Call tool_describe before the first invocation of any unfamiliar tool (MCP or built-in). "
            + (capabilities.PreferSkillDiscovery
                ? "Skills: use skill_search then skill_read. "
                : "Critical evidence rules are inlined below; other skills via skill_search / skill_read. ")
            + "Requestable rules: rule_search / rule_read. "
            + "Heavy research: delegate_task (depth 1). "
            + "After tool results, answer in the user's language with requested fields only — do not dump raw tool JSON.");

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

        if (capabilities.InlineEvidenceRules)
        {
            sb.AppendLine();
            sb.AppendLine("## Evidence rules (mandatory)");
            sb.AppendLine(
                "- Do not invent IDs, account numbers, statuses, amounts, or dates.");
            sb.AppendLine(
                "- If live data is missing, call MCP/wiki tools first; never guess.");
            sb.AppendLine(
                "- If a tool fails, report the failure; do not fabricate a substitute answer.");
            sb.AppendLine(
                "- Final answer must use only facts observed in tool results.");

            AppendEvidenceSkillBodies(sb, runtimeConfig, maxChars: 800);
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
        if (defaults.Count > 0 && capabilities.PreferSkillDiscovery)
        {
            sb.AppendLine();
            sb.AppendLine("## Default skills (ids — skill_search / skill_read for more)");
            foreach (var skill in defaults)
                sb.AppendLine($"- `{skill.Id}`: {skill.Name}");
        }
        else if (skills.Count > 0 && capabilities.PreferSkillDiscovery)
        {
            sb.AppendLine();
            sb.AppendLine("Skills available via skill_search / skill_read (not inlined).");
        }
        else if (defaults.Count > 0 && !capabilities.PreferSkillDiscovery)
        {
            sb.AppendLine();
            sb.AppendLine("## Other skill ids (optional — skill_read)");
            foreach (var skill in defaults.Where(s => !IsEvidenceSkill(s.Id)).Take(3))
                sb.AppendLine($"- `{skill.Id}`: {skill.Name}");
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

    private static void AppendEvidenceSkillBodies(
        StringBuilder sb,
        AppRuntimeConfig runtimeConfig,
        int maxChars)
    {
        var evidence = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => s.IsDefaultEnabled && IsEvidenceSkill(s.Id))
            .OrderBy(s => s.SortOrder)
            .Take(3)
            .ToList();
        if (evidence.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Evidence skills (inlined)");
        foreach (var skill in evidence)
        {
            sb.AppendLine($"### {skill.Name} (`{skill.Id}`)");
            var body = string.IsNullOrWhiteSpace(skill.PromptMarkdown)
                ? skill.Description
                : skill.PromptMarkdown.Trim();
            if (body.Length > maxChars)
                body = body[..maxChars] + "…";
            sb.AppendLine(body);
        }
    }

    private static bool IsEvidenceSkill(string skillId) =>
        skillId.Contains("evidence", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("ground", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("prefer-mcp", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("tool-calling", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("anti-hallucination", StringComparison.OrdinalIgnoreCase);

    private static bool IsMcpRelevantSkill(string skillId) =>
        skillId.Contains("mcp", StringComparison.OrdinalIgnoreCase)
        || skillId.Contains("zuora", StringComparison.OrdinalIgnoreCase)
        || IsEvidenceSkill(skillId);
}
