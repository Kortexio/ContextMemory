using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface ICapabilityPolicyFilter
{
    IReadOnlyList<OllamaTool> FilterTools(
        IEnumerable<OllamaTool> tools,
        CapabilityPolicy policy);
}
