using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

/// <summary>
/// Localized OpenAI function schema descriptions exposed to the LLM.
/// </summary>
public static class ToolSchemaMessages
{
    public static string ShellCommand(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Shell command to run in the isolated environment.",
            "Comando shell a executar no ambiente isolado.");

    public static string PythonCode(AppRuntimeConfig config, bool selfHosted = false) =>
        selfHosted
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "Python code to run in the self-hosted sandbox (gVisor).",
                "Código Python a executar no sandbox self-hosted (gVisor).")
            : TenantLocale.Select(
                config.DefaultLanguage,
                "Python code to run in the isolated environment.",
                "Código Python a executar no ambiente isolado.");

    public static string NodeCode(AppRuntimeConfig config, bool selfHosted = false) =>
        selfHosted
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "JavaScript/Node code to run in the self-hosted sandbox (gVisor).",
                "Código JavaScript/Node a executar no sandbox self-hosted (gVisor).")
            : TenantLocale.Select(
                config.DefaultLanguage,
                "JavaScript/Node code to run in the isolated environment.",
                "Código JavaScript/Node a executar no ambiente isolado.");

    public static string ContainerCommand(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "Command to run in the custom container.",
            "Comando a executar no container custom.");
}
