using System.Text;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// CodeForge-style tool system contract for active tools only.
/// Does not replace Admin skills / policy prose.
/// </summary>
public static class ToolSystemContractBuilder
{
    public static string Build(IEnumerable<string> activeToolNames)
    {
        var active = new HashSet<string>(
            activeToolNames.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (active.Count == 0)
            return string.Empty;

        bool Has(params string[] names) => names.Any(active.Contains);

        var sb = new StringBuilder();
        sb.AppendLine("### Tool contract");
        sb.AppendLine("Call tools to inspect live data and act. Chat text alone is not evidence.");

        var explore = new List<string>();
        if (Has("wiki_search")) explore.Add("wiki_search");
        if (Has("wiki_grep")) explore.Add("wiki_grep");
        if (Has("tool_search")) explore.Add("tool_search");
        if (Has("tool_describe")) explore.Add("tool_describe");
        if (Has("skill_search", "skill_read")) explore.Add("skill_search/skill_read");
        if (explore.Count > 0)
            sb.AppendLine($"Discovery: {string.Join(", ", explore)}.");

        var exec = new List<string>();
        if (Has("shell_execute")) exec.Add("shell_execute");
        if (Has("python_execute")) exec.Add("python_execute");
        if (Has("node_execute")) exec.Add("node_execute");
        if (Has("container_execute")) exec.Add("container_execute");
        if (exec.Count > 0)
            sb.AppendLine($"Execution: {string.Join(", ", exec)}.");

        if (Has("http_fetch", "http_request", "web_fetch"))
            sb.AppendLine("HTTP: use dedicated fetch tools for URLs; do not invent page contents.");

        if (Has("browser_navigate", "browser_snapshot", "browser_click"))
            sb.AppendLine("Browser: observe (snapshot) → act → re-observe. Unchanged UI: do not repeat the same selector.");

        sb.AppendLine(
            "Schemas for listed tools are already in the catalog — call them directly. "
            + "Use tool_describe only for tools outside the offered set or when a schema is still unclear.");
        sb.AppendLine(
            "Do not repeat an identical successful tool+args call. After a failure, change arguments or pivot.");
        sb.AppendLine(
            "Finish: discovery-only (tool_describe/skill_search/…) is not done when the user needs live facts. "
            + "Gather evidence with wiki/MCP/http/sandbox, then answer. Never narrate harness or raw tool names to the user.");
        sb.AppendLine("Answer in the user's language.");

        return sb.ToString().TrimEnd();
    }
}
