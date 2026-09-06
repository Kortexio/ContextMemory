using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Subagent;

public interface ISubagentOrchestrator
{
    Task<SubagentResult> RunAsync(
        SubagentParentContext parent,
        SubagentSpec spec,
        string objective,
        CancellationToken cancellationToken = default);

    Task<SubagentParallelAggregate> RunParallelAsync(
        SubagentParentContext parent,
        IReadOnlyList<(SubagentSpec Spec, string Objective)> tasks,
        CancellationToken cancellationToken = default);

    /// <summary>Counts nesting depth from session id markers (<c>:sub:</c> / <c>/sub/</c>).</summary>
    static int GetSessionDepth(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return 0;

        var depth = 0;
        var idx = 0;
        while (idx < sessionId.Length)
        {
            var a = sessionId.IndexOf(":sub:", idx, StringComparison.OrdinalIgnoreCase);
            var b = sessionId.IndexOf("/sub/", idx, StringComparison.OrdinalIgnoreCase);
            if (a < 0 && b < 0)
                break;
            var next = a < 0 ? b : b < 0 ? a : Math.Min(a, b);
            depth++;
            idx = next + 5;
        }

        return depth;
    }
}
