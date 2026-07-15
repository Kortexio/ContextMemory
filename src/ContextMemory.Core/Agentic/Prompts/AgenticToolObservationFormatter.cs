using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticToolObservationFormatter
{
    public static string Format(
        string toolName,
        ToolExecutionResult result,
        AppRuntimeConfig config)
    {
        return AgenticPromptProfileResolver.Resolve(config) switch
        {
            AgenticPromptProfile.OpenAi =>
                $"Function `{toolName}` returned (exit_code={result.ExitCode}):\n{result.Output}",
            AgenticPromptProfile.Claude =>
                $"Resultado da tool `{toolName}` (exit={result.ExitCode}):\n{result.Output}",
            _ =>
                $"[{toolName}] exit_code={result.ExitCode}\n{result.Output}"
        };
    }
}
