using ContextMemory.Core.Agentic;

namespace ContextMemory.Core.Contracts;

public interface IContextPolicyGate
{
    string FilterPromptSection(string content, ContextPolicy policy);
}
