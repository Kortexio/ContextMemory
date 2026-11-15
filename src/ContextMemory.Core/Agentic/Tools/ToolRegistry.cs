using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Aggregates <see cref="IToolDescriptor"/> instances from DI for schema building.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    public ToolRegistry(IEnumerable<IToolDescriptor> descriptors) =>
        Descriptors = descriptors.ToList();

    public IReadOnlyList<IToolDescriptor> Descriptors { get; }

    public IEnumerable<OllamaTool> BuildBuiltinSchemas(AppRuntimeConfig runtimeConfig)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in Descriptors)
        {
            foreach (var tool in descriptor.BuildSchemas(runtimeConfig))
            {
                var name = tool.Function.Name;
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;
                yield return tool;
            }
        }
    }
}
