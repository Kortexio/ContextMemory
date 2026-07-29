using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticToolObservationFormatter
{
    public static string Format(
        string toolName,
        ToolExecutionResult result,
        AppRuntimeConfig config)
    {
        var payload = result.Summary ?? result.Output;
        if (result.Entities is { Count: > 0 })
        {
            var entityLine = string.Join(", ", result.Entities.Select(kv => $"{kv.Key}={kv.Value}"));
            payload = $"{payload}\nEntities: {entityLine}";
        }

        return AgenticPromptProfileResolver.Resolve(config) switch
        {
            AgenticPromptProfile.OpenAi =>
                $"Function `{toolName}` returned (exit_code={result.ExitCode}):\n{payload}",
            AgenticPromptProfile.Claude =>
                $"Resultado da tool `{toolName}` (exit={result.ExitCode}):\n{payload}",
            _ =>
                $"[{toolName}] exit_code={result.ExitCode}\n{payload}"
        };
    }
}
