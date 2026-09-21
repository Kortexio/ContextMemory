using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

public static class ValidationMessages
{
    public static string EmptyFinalAnswer(AppRuntimeConfig config)
    {
        _ = config;
        return "The final answer is empty. Provide a complete response to the user.";
    }

    public static string TooShort(int minLength, AppRuntimeConfig config)
    {
        _ = config;
        return $"The answer is too short (minimum {minLength} characters). Add more relevant detail.";
    }

    public static string BlockedContent(string pattern, AppRuntimeConfig config)
    {
        _ = config;
        return $"The answer contains content blocked by the tenant guardrail ('{pattern}'). Rephrase without it.";
    }

    public static string ToolsFailedExitCode(string toolList, AppRuntimeConfig config)
    {
        _ = config;
        return $"One or more tools finished with exit code != 0 ({toolList}). Fix the issue or explain the error clearly.";
    }

    public static string ToolsFailedNotMentioned(string toolList, AppRuntimeConfig config)
    {
        _ = config;
        return $"One or more tools failed ({toolList}) but the answer does not mention the error. Explain what went wrong and what the user can do.";
    }

    public static string PatternMismatch(string pattern, AppRuntimeConfig config)
    {
        _ = config;
        return $"The final answer does not match the tenant's expected pattern (`{pattern}`). Adjust the content to meet the agreed format.";
    }

    public static string ConfirmationRequired(string keyword, AppRuntimeConfig config)
    {
        _ = config;
        return $"The action related to '{keyword}' requires human confirmation. Ask for explicit user confirmation before proceeding.";
    }

    public static string FabricatedSandboxLimitation(string feedback, AppRuntimeConfig config)
    {
        _ = config;
        return string.IsNullOrWhiteSpace(feedback)
            ? "Rejected fabricated sandbox limitation. Call real tools instead."
            : feedback;
    }

    public static string UrlDescribedWithoutFetch(string feedback, AppRuntimeConfig config)
    {
        _ = config;
        return string.IsNullOrWhiteSpace(feedback)
            ? "Rejected: website described without fetching it. Call tools first."
            : feedback;
    }

    public static string LiveDataWithoutEvidence(string feedback, AppRuntimeConfig config)
    {
        _ = config;
        return string.IsNullOrWhiteSpace(feedback)
            ? "Rejected: live-data answer without MCP/wiki evidence. Emit tool_calls; do not invent."
            : feedback;
    }

    public static string ToolIntentNarration(string feedback, AppRuntimeConfig config)
    {
        _ = config;
        return string.IsNullOrWhiteSpace(feedback)
            ? "Rejected: narrated tool intent instead of emitting tool_calls. Call tools now."
            : feedback;
    }
}
