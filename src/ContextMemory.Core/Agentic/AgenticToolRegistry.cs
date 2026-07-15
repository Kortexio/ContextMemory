using ContextMemory.Core.Models;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Agentic;

public static class AgenticToolRegistry
{
    public const string ShellExecuteToolName = "shell_execute";
    public const string PythonExecuteToolName = "python_execute";
    public const string NodeExecuteToolName = "node_execute";
    public const string ContainerExecuteToolName = "container_execute";

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig) =>
        BuildExecutionTools(runtimeConfig);

    public static List<OllamaTool> BuildExecutionTools(AppRuntimeConfig runtimeConfig)
    {
        var tools = new List<OllamaTool>();

        foreach (var execution in runtimeConfig.Agentic.Tools.Execution)
        {
            if (string.Equals(execution.Type, "aca-session", StringComparison.OrdinalIgnoreCase))
            {
                AddAcaExecutionTool(tools, runtimeConfig, execution);
            }
            else if (string.Equals(execution.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase))
            {
                AddSelfHostedExecutionTool(tools, runtimeConfig, execution);
            }
        }

        return tools;
    }

    private static void AddAcaExecutionTool(
        List<OllamaTool> tools,
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution)
    {
        if (string.Equals(execution.Runtime, "shell", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildShellTool(runtimeConfig));
        }
        else if (string.Equals(execution.Runtime, "python", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildCodeTool(
                PythonExecuteToolName,
                AgenticToolDescriptionBuilder.BuildPythonDescription(runtimeConfig),
                ToolSchemaMessages.PythonCode(runtimeConfig)));
        }
        else if (string.Equals(execution.Runtime, "node", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildCodeTool(
                NodeExecuteToolName,
                AgenticToolDescriptionBuilder.BuildNodeDescription(runtimeConfig),
                ToolSchemaMessages.NodeCode(runtimeConfig)));
        }
        else if (string.Equals(execution.Runtime, "custom", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildContainerTool(runtimeConfig, execution));
        }
    }

    private static void AddSelfHostedExecutionTool(
        List<OllamaTool> tools,
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution)
    {
        if (string.Equals(execution.Runtime, "shell", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, ShellExecuteToolName, StringComparison.Ordinal)))
                tools.Add(BuildShellTool(runtimeConfig));
        }
        else if (string.Equals(execution.Runtime, "python", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, PythonExecuteToolName, StringComparison.Ordinal)))
            {
                tools.Add(BuildCodeTool(
                    PythonExecuteToolName,
                    AgenticToolDescriptionBuilder.BuildPythonDescription(runtimeConfig),
                    ToolSchemaMessages.PythonCode(runtimeConfig, selfHosted: true)));
            }
        }
        else if (string.Equals(execution.Runtime, "node", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, NodeExecuteToolName, StringComparison.Ordinal)))
            {
                tools.Add(BuildCodeTool(
                    NodeExecuteToolName,
                    AgenticToolDescriptionBuilder.BuildNodeDescription(runtimeConfig),
                    ToolSchemaMessages.NodeCode(runtimeConfig, selfHosted: true)));
            }
        }
    }

    private static OllamaTool BuildShellTool(AppRuntimeConfig runtimeConfig) =>
        new(
            "function",
            new OllamaFunction(
                ShellExecuteToolName,
                AgenticToolDescriptionBuilder.BuildShellDescription(runtimeConfig),
                new
                {
                    type = "object",
                    properties = new
                    {
                        command = new
                        {
                            type = "string",
                            description = ToolSchemaMessages.ShellCommand(runtimeConfig)
                        }
                    },
                    required = new[] { "command" }
                }));

    private static OllamaTool BuildCodeTool(string name, string description, string codeDescription) =>
        new(
            "function",
            new OllamaFunction(
                name,
                description,
                new
                {
                    type = "object",
                    properties = new
                    {
                        code = new
                        {
                            type = "string",
                            description = codeDescription
                        }
                    },
                    required = new[] { "code" }
                }));

    private static OllamaTool BuildContainerTool(AppRuntimeConfig runtimeConfig, ExecutionToolConfig execution) =>
        new(
            "function",
            new OllamaFunction(
                ContainerExecuteToolName,
                AgenticToolDescriptionBuilder.BuildContainerDescription(runtimeConfig, execution),
                new
                {
                    type = "object",
                    properties = new
                    {
                        command = new
                        {
                            type = "string",
                            description = ToolSchemaMessages.ContainerCommand(runtimeConfig)
                        }
                    },
                    required = new[] { "command" }
                }));

    public static string BuildAgenticSystemPrompt(AppRuntimeConfig runtimeConfig, string toolNamesSummary) =>
        AgenticSystemPromptBuilder.Build(runtimeConfig, toolNamesSummary);
}
