using System.Text;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Tool system contract for active tools, driven by <see cref="ToolCapabilities"/> — not wire-name lists.
/// </summary>
public static class ToolSystemContractBuilder
{
    public static string Build(IEnumerable<string> activeToolNames)
    {
        var tools = activeToolNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n =>
            {
                ToolCapabilityIndex.TryGet(n, out var caps);
                return (Name: n.Trim(), Caps: caps);
            })
            .ToList();

        return Build(tools);
    }

    public static string Build(IEnumerable<(string Name, ToolCapabilities Caps)> activeTools)
    {
        var tools = activeTools
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (tools.Count == 0)
            return string.Empty;

        var union = ToolCapabilities.None;
        foreach (var t in tools)
            union |= t.Caps;

        var sb = new StringBuilder();
        sb.AppendLine("### Tool contract");
        sb.AppendLine("Call tools to inspect live data and act. Chat text alone is not evidence.");

        var explore = tools
            .Where(t => t.Caps.HasFlag(ToolCapabilities.Discovery) || t.Caps.HasFlag(ToolCapabilities.QueryFocused))
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (explore.Count > 0)
            sb.AppendLine($"Discovery: {string.Join(", ", explore)}.");

        var exec = tools
            .Where(t => t.Caps.HasFlag(ToolCapabilities.Sandbox))
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exec.Count > 0)
            sb.AppendLine($"Execution: {string.Join(", ", exec)}.");

        if (union.HasFlag(ToolCapabilities.Http))
            sb.AppendLine("HTTP: use dedicated fetch tools for URLs; do not invent page contents.");

        if (union.HasFlag(ToolCapabilities.Browser))
            sb.AppendLine("Browser: observe (snapshot) → act → re-observe. Unchanged UI: do not repeat the same selector.");

        sb.AppendLine(
            "Schemas for listed tools are already in the catalog — call them directly. "
            + "Use discovery helpers only for tools outside the offered set or when a schema is still unclear.");
        sb.AppendLine(
            "Do not repeat an identical successful tool+args call. After a failure, change arguments or pivot.");
        sb.AppendLine(
            "Finish: discovery-only turns are not done when the user needs live facts. "
            + "Gather evidence with query/MCP/http/sandbox tools, then answer. Never narrate harness or raw tool names to the user.");
        sb.AppendLine("Answer in the user's language.");

        return sb.ToString().TrimEnd();
    }
}
