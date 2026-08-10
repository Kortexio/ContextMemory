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
    public const string WikiSearchToolName = "wiki_search";
    public const string WikiGrepToolName = "wiki_grep";

    private static readonly object OpenParameters = new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>(),
        ["additionalProperties"] = true
    };

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig) =>
        BuildExecutionTools(runtimeConfig, lazySchemas: true);

    public static OllamaTool? BuildWikiSearchTool(AppRuntimeConfig runtimeConfig, bool lazySchemas = true)
    {
        if (!runtimeConfig.GlobalWikiEnabled)
            return null;

        if (lazySchemas)
        {
            return new OllamaTool(
                "function",
                new OllamaFunction(
                    WikiSearchToolName,
                    "Search app knowledge base. Call tool_describe first for full args schema.",
                    OpenParameters));
        }

        return new OllamaTool(
            "function",
            new OllamaFunction(
                WikiSearchToolName,
                AgenticToolDescriptionBuilder.BuildWikiSearchDescription(runtimeConfig),
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "Search query against the app's global knowledge base (ingested docs)."
                        },
                        sourceId = new
                        {
                            type = "string",
                            description = "Optional source filter (e.g. jira, confluence:SPACE)."
                        },
                        topK = new
                        {
                            type = "integer",
                            description = "Max documents to return (default 5)."
                        },
                        asOf = new
                        {
                            type = "string",
                            description = "Optional ISO-8601 timestamp for point-in-time facts (what was true at this moment). Default = now."
                        }
                    },
                    required = new[] { "query" }
                }));
    }

    public static OllamaTool? BuildWikiGrepTool(AppRuntimeConfig runtimeConfig, bool lazySchemas = true)
    {
        if (!runtimeConfig.GlobalWikiEnabled)
            return null;

        if (lazySchemas)
        {
            return new OllamaTool(
                "function",
                new OllamaFunction(
                    WikiGrepToolName,
                    "Regex search over wiki Content/Summary. Call tool_describe first for args.",
                    OpenParameters));
        }

        return new OllamaTool(
            "function",
            new OllamaFunction(
                WikiGrepToolName,
                "Regex search (case-insensitive) over Global Wiki document content and summaries.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = ".NET/ECMAScript-compatible regex pattern"
                        },
                        sourceId = new
                        {
                            type = "string",
                            description = "Optional source filter"
                        },
                        maxHits = new
                        {
                            type = "integer",
                            description = "Max match lines to return (default 40, max 200)"
                        },
                        asOf = new
                        {
                            type = "string",
                            description = "Optional ISO-8601 timestamp for point-in-time docs"
                        }
                    },
                    required = new[] { "pattern" }
                }));
    }

    public static List<OllamaTool> BuildExecutionTools(AppRuntimeConfig runtimeConfig, bool lazySchemas = true)
    {
        var tools = new List<OllamaTool>();

        foreach (var execution in runtimeConfig.Agentic.Tools.Execution)
        {
            if (string.Equals(execution.Type, "aca-session", StringComparison.OrdinalIgnoreCase))
            {
                AddAcaExecutionTool(tools, runtimeConfig, execution, lazySchemas);
            }
            else if (string.Equals(execution.Type, "self-hosted-sandbox", StringComparison.OrdinalIgnoreCase))
            {
                AddSelfHostedExecutionTool(tools, runtimeConfig, execution, lazySchemas);
            }
        }

        return tools;
    }

    private static void AddAcaExecutionTool(
        List<OllamaTool> tools,
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution,
        bool lazySchemas)
    {
        if (string.Equals(execution.Runtime, "shell", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildShellTool(runtimeConfig, execution, lazySchemas));
        }
        else if (string.Equals(execution.Runtime, "python", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildCodeTool(
                PythonExecuteToolName,
                AgenticToolDescriptionBuilder.BuildPythonDescription(runtimeConfig, execution),
                ToolSchemaMessages.PythonCode(runtimeConfig),
                lazySchemas));
        }
        else if (string.Equals(execution.Runtime, "node", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildCodeTool(
                NodeExecuteToolName,
                AgenticToolDescriptionBuilder.BuildNodeDescription(runtimeConfig, execution),
                ToolSchemaMessages.NodeCode(runtimeConfig),
                lazySchemas));
        }
        else if (string.Equals(execution.Runtime, "custom", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(BuildContainerTool(runtimeConfig, execution, lazySchemas));
        }
    }

    private static void AddSelfHostedExecutionTool(
        List<OllamaTool> tools,
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution,
        bool lazySchemas)
    {
        if (string.Equals(execution.Runtime, "shell", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, ShellExecuteToolName, StringComparison.Ordinal)))
                tools.Add(BuildShellTool(runtimeConfig, execution, lazySchemas));
        }
        else if (string.Equals(execution.Runtime, "python", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, PythonExecuteToolName, StringComparison.Ordinal)))
            {
                tools.Add(BuildCodeTool(
                    PythonExecuteToolName,
                    AgenticToolDescriptionBuilder.BuildPythonDescription(runtimeConfig, execution),
                    ToolSchemaMessages.PythonCode(runtimeConfig, selfHosted: true),
                    lazySchemas));
            }
        }
        else if (string.Equals(execution.Runtime, "node", StringComparison.OrdinalIgnoreCase))
        {
            if (!tools.Any(t => string.Equals(t.Function.Name, NodeExecuteToolName, StringComparison.Ordinal)))
            {
                tools.Add(BuildCodeTool(
                    NodeExecuteToolName,
                    AgenticToolDescriptionBuilder.BuildNodeDescription(runtimeConfig, execution),
                    ToolSchemaMessages.NodeCode(runtimeConfig, selfHosted: true),
                    lazySchemas));
            }
        }
    }

    private static OllamaTool BuildShellTool(
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig? execution,
        bool lazySchemas)
    {
        if (lazySchemas)
        {
            return new OllamaTool(
                "function",
                new OllamaFunction(
                    ShellExecuteToolName,
                    "Run a shell command in the sandbox. Call tool_describe first for args.",
                    OpenParameters));
        }

        return new(
            "function",
            new OllamaFunction(
                ShellExecuteToolName,
                AgenticToolDescriptionBuilder.BuildShellDescription(runtimeConfig, execution),
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
    }

    private static OllamaTool BuildCodeTool(
        string name,
        string description,
        string codeDescription,
        bool lazySchemas)
    {
        if (lazySchemas)
        {
            return new OllamaTool(
                "function",
                new OllamaFunction(
                    name,
                    $"{name}: execute code in sandbox. Call tool_describe first for args.",
                    OpenParameters));
        }

        return new(
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
    }

    private static OllamaTool BuildContainerTool(
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution,
        bool lazySchemas)
    {
        if (lazySchemas)
        {
            return new OllamaTool(
                "function",
                new OllamaFunction(
                    ContainerExecuteToolName,
                    "Run a command in a custom container. Call tool_describe first for args.",
                    OpenParameters));
        }

        return new(
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
    }

    public static string BuildAgenticSystemPrompt(AppRuntimeConfig runtimeConfig, string toolNamesSummary) =>
        AgenticSystemPromptBuilder.Build(runtimeConfig, toolNamesSummary);
}
