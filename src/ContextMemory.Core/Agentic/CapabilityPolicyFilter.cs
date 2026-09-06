using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public sealed class CapabilityPolicyFilter : ICapabilityPolicyFilter
{
    public IReadOnlyList<OllamaTool> FilterTools(
        IEnumerable<OllamaTool> tools,
        CapabilityPolicy policy)
    {
        policy ??= new CapabilityPolicy();
        var allowed = policy.AllowedToolPatterns ?? ["*"];
        var denied = policy.DeniedToolPatterns ?? [];

        // Default open policy: keep everything.
        if (IsFullyOpen(allowed, denied))
            return tools as IReadOnlyList<OllamaTool> ?? tools.ToList();

        var result = new List<OllamaTool>();
        foreach (var tool in tools)
        {
            var name = tool.Function.Name;
            if (PolicyGlob.MatchesAny(name, denied))
                continue;

            if (!PolicyGlob.MatchesAny(name, allowed))
                continue;

            result.Add(tool);
        }

        return result;
    }

    private static bool IsFullyOpen(IReadOnlyList<string> allowed, IReadOnlyList<string> denied) =>
        denied.Count == 0
        && allowed.Count == 1
        && string.Equals(allowed[0], "*", StringComparison.Ordinal);
}
