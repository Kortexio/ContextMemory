using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

internal static class AgentInstructionInjector
{
    public static List<OllamaMessage> Inject(
        IReadOnlyList<OllamaMessage> messages,
        AppRuntimeConfig runtimeConfig,
        string toolNamesSummary)
    {
        var agenticPrompt = AgenticToolRegistry.BuildAgenticSystemPrompt(runtimeConfig, toolNamesSummary);
        if (string.IsNullOrWhiteSpace(agenticPrompt))
            return messages.ToList();

        var result = new List<OllamaMessage>();
        var systemInjected = false;

        foreach (var message in messages)
        {
            if (!systemInjected && string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(message with { Content = message.Content + "\n\n" + agenticPrompt });
                systemInjected = true;
            }
            else
            {
                result.Add(message);
            }
        }

        if (!systemInjected)
            result.Insert(0, new OllamaMessage { Role = "system", Content = agenticPrompt });

        return result;
    }
}
