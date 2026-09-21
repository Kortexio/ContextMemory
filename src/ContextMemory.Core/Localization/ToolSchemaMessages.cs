using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// OpenAI function schema descriptions exposed to the LLM (English only).
/// </summary>
public static class ToolSchemaMessages
{
    public static string ShellCommand(AppRuntimeConfig config) =>
        "Shell command to run in the isolated environment.";

    public static string PythonCode(AppRuntimeConfig config, bool selfHosted = false) =>
        selfHosted
            ? "Python source to run in the self-hosted sandbox (HTTP egress allowed; ephemeral files)."
            : "Python code to run in the isolated ACA environment.";

    public static string NodeCode(AppRuntimeConfig config, bool selfHosted = false) =>
        selfHosted
            ? "JavaScript/Node source to run in the self-hosted sandbox (HTTP egress allowed; ephemeral files)."
            : "JavaScript/Node code to run in the isolated ACA environment.";

    public static string ContainerCommand(AppRuntimeConfig config) =>
        "Command to run in the custom container.";
}
