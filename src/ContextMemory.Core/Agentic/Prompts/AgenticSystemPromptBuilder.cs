using System.Text;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

/// <summary>
/// Builds a thin harness scaffold + CodeForge-style tool contract.
/// Policy prose lives in Admin skills (always-on / skill_read).
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
            "Listed tools already include schemas — call them directly. "
            + "Use tool_describe only for tools outside the offered set. "
            + "Long outputs via artifact_tail/artifact_read. "
            + "Skills/rules via skill_search|skill_read / rule_search|rule_read. "
            + "Never narrate harness or tool names to the user. "
            + "Answer in the user's language.");

        var activeNames = ParseToolNames(toolNamesSummary);
        var contract = ToolSystemContractBuilder.Build(activeNames);
        if (!string.IsNullOrWhiteSpace(contract))
        {
            sb.AppendLine();
            sb.AppendLine(contract);
        }

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

    /// <summary>Best-effort extraction of tool ids from the catalog summary line.</summary>
    internal static IEnumerable<string> ParseToolNames(string toolNamesSummary)
    {
        foreach (var part in toolNamesSummary.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = part;
            var tick = token.IndexOf('`');
            if (tick >= 0)
            {
                var end = token.IndexOf('`', tick + 1);
                if (end > tick)
                    token = token[(tick + 1)..end];
            }

            var colon = token.IndexOf(':');
            if (colon > 0 && !token.Contains("__", StringComparison.Ordinal))
            {
                // "zuora: query_objects, graphql" style — yield server-qualified guesses later via summary only
                foreach (var leaf in token[(colon + 1)..].Split(['/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (leaf.Length > 1)
                        yield return leaf.TrimEnd('.');
                }
            }

            if (token.Contains("__", StringComparison.Ordinal) || !token.Contains(' '))
            {
                var name = token.Split(' ', 2)[0].Trim().TrimEnd('.');
                if (name.Length > 1)
                    yield return name;
            }
        }

        // Always include common builtins so the contract sections light up.
        foreach (var builtin in new[]
                 {
                     "wiki_search", "wiki_fetch", "wiki_grep", "tool_describe", "tool_search",
                     "skill_search", "skill_read", "shell_execute", "python_execute",
                     "node_execute", "artifact_read", "artifact_tail"
                 })
            yield return builtin;
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
