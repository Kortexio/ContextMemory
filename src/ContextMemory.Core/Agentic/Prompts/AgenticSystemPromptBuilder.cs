using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

/// <summary>
/// Builds a thin harness scaffold. Policy prose lives in Admin skills (always-on / skill_read) —
/// this class must not re-state MCP/evidence/sandbox rules that already exist in the catalog.
/// </summary>
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
        var hasMcp = runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(i.Name));

        var sb = new StringBuilder();
        sb.AppendLine("## Agentic mode");
        sb.AppendLine($"Harness profile: {profile} ({capabilities.HarnessMode}).");
        sb.AppendLine($"Available tools: {toolNamesSummary}.");
        sb.AppendLine(
            "Discover schemas with tool_describe; long outputs via artifact_tail/artifact_read. "
            + "Skills/rules via skill_search|skill_read / rule_search|rule_read. "
            + "Never narrate harness or tool names to the user. "
            + "Answer in the user's language.");

        if (capabilities.InlineEvidenceRules)
            AppendSkillBodies(sb, SelectEvidenceSkills(runtimeConfig), "Evidence skills (inlined)", 800);
        else
            AppendDefaultSkillIds(sb, runtimeConfig, hasMcp, capabilities.PreferSkillDiscovery);

        AppendSkillBodies(
            sb,
            runtimeConfig.ResolvedPolicy.ActiveSkills
                .Where(s => AgenticSkillActivation.IsAlwaysOn(s.Activation) && s.IsDefaultEnabled)
                .OrderBy(s => s.SortOrder)
                .ToList(),
            "Always-on rules",
            1200);

        var requestable = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Count(s => AgenticSkillActivation.IsRequestable(s.Activation) && s.IsDefaultEnabled);
        if (requestable > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Requestable rules: {requestable} via rule_search / rule_read.");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendDefaultSkillIds(
        StringBuilder sb,
        AppRuntimeConfig runtimeConfig,
        bool hasMcp,
        bool preferDiscovery)
    {
        var skills = runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => AgenticSkillActivation.IsSkill(s.Activation))
            .ToList();
        var defaults = skills
            .Where(s => s.IsDefaultEnabled)
            .OrderByDescending(s => hasMcp && IsMcpRelevantSkill(s.Id))
            .ThenBy(s => s.SortOrder)
            .Take(5)
            .ToList();

        if (defaults.Count == 0)
        {
            if (skills.Count > 0 && preferDiscovery)
            {
                sb.AppendLine();
                sb.AppendLine("Skills available via skill_search / skill_read.");
            }

            return;
        }

        if (!preferDiscovery)
        {
            sb.AppendLine();
            sb.AppendLine("## Other skill ids (optional — skill_read)");
            foreach (var skill in defaults.Where(s => !IsEvidenceSkill(s.Id)).Take(3))
                sb.AppendLine($"- `{skill.Id}`: {skill.Name}");
            return;
        }

        sb.AppendLine();
        sb.AppendLine("## Default skills (ids — skill_search / skill_read for more)");
        foreach (var skill in defaults)
            sb.AppendLine($"- `{skill.Id}`: {skill.Name}");
    }

    private static IReadOnlyList<AgenticSkillDefinition> SelectEvidenceSkills(AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.ResolvedPolicy.ActiveSkills
            .Where(s => s.IsDefaultEnabled && IsEvidenceSkill(s.Id))
            .OrderBy(s => s.SortOrder)
            .Take(3)
            .ToList();

    private static void AppendSkillBodies(
        StringBuilder sb,
        IReadOnlyList<AgenticSkillDefinition> skills,
        string heading,
        int maxChars)
    {
        if (skills.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        foreach (var skill in skills)
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
        || IsEvidenceSkill(skillId);
}
