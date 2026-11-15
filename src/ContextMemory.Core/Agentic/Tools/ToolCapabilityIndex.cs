using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Process-wide capability map published by <see cref="ToolResolver"/>.
/// Static gates consult this instead of hardcoding wire names.
/// </summary>
public static class ToolCapabilityIndex
{
    private static IReadOnlyDictionary<string, ToolCapabilities> _map =
        new Dictionary<string, ToolCapabilities>(StringComparer.OrdinalIgnoreCase);

    public static void Publish(IReadOnlyDictionary<string, ToolCapabilities> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = new Dictionary<string, ToolCapabilities>(map, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGet(string? wireName, out ToolCapabilities capabilities)
    {
        capabilities = ToolCapabilities.None;
        if (string.IsNullOrWhiteSpace(wireName))
            return false;

        return _map.TryGetValue(wireName.Trim(), out capabilities);
    }

    public static bool HasFlag(string? wireName, ToolCapabilities flag) =>
        TryGet(wireName, out var caps) && caps.HasFlag(flag);

    /// <summary>
    /// Prefer stamped step capabilities; fall back to the published index for legacy/test steps.
    /// </summary>
    public static bool StepHas(AgentExecutionStep step, ToolCapabilities flag)
    {
        if (step.Capabilities != ToolCapabilities.None)
            return step.Capabilities.HasFlag(flag);

        return HasFlag(step.ToolName, flag);
    }
}
