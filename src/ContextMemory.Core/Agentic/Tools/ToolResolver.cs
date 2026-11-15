using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Builds a wire-name map from <see cref="IToolDescriptor"/> registrations; MCP qualified names use a dynamic fallback.
/// </summary>
public sealed class ToolResolver : IToolResolver
{
    private readonly IReadOnlyDictionary<string, ToolRegistration> _registrations;

    public ToolResolver(IEnumerable<IToolDescriptor> descriptors)
    {
        var map = new Dictionary<string, ToolRegistration>(StringComparer.OrdinalIgnoreCase);
        var caps = new Dictionary<string, ToolCapabilities>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            foreach (var reg in descriptor.Describe())
            {
                if (string.IsNullOrWhiteSpace(reg.WireName))
                    continue;

                var name = reg.WireName.Trim();
                map[name] = reg with { WireName = name };
                caps[name] = reg.Capabilities;
            }
        }

        _registrations = map;
        ToolCapabilityIndex.Publish(caps);
    }

    public bool TryResolve(string? wireName, AppRuntimeConfig runtimeConfig, out ResolvedTool tool)
    {
        tool = null!;
        if (string.IsNullOrWhiteSpace(wireName))
            return false;

        var name = wireName.Trim();

        if (_registrations.TryGetValue(name, out var reg))
        {
            tool = new ResolvedTool
            {
                WireName = reg.WireName,
                Capabilities = reg.Capabilities,
                Kind = reg.Kind,
                IsMcp = false
            };
            return true;
        }

        if (TryResolveMcp(name, runtimeConfig, out tool))
            return true;

        return false;
    }

    public bool HasCapability(string? wireName, ToolCapabilities flag)
    {
        if (string.IsNullOrWhiteSpace(wireName))
            return false;

        if (_registrations.TryGetValue(wireName.Trim(), out var reg))
            return reg.Capabilities.HasFlag(flag);

        return false;
    }

    private static bool TryResolveMcp(string name, AppRuntimeConfig runtimeConfig, out ResolvedTool tool)
    {
        tool = null!;
        if (!McpToolNaming.TryParseQualifiedName(name, out var serverName, out _))
            return false;

        var enabled = runtimeConfig.Agentic.Tools.Integrations.Any(i =>
            string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase)
            && i.Enabled
            && McpToolNaming.ServerNamesMatch(i.Name, serverName));

        if (!enabled)
            return false;

        tool = new ResolvedTool
        {
            WireName = name,
            Capabilities = ToolCapabilities.None,
            Kind = ToolHandlerKind.AppScoped,
            IsMcp = true
        };
        return true;
    }
}
