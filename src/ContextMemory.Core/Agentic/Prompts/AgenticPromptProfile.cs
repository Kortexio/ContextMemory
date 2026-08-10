using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public enum AgenticPromptProfile
{
    Ollama,
    OpenAi,
    Claude,
    Qwen,
    ComposerLike
}

public static class AgenticPromptProfileResolver
{
    public static AgenticPromptProfile Resolve(AppRuntimeConfig config)
    {
        var explicitProfile = config.Agentic.PromptProfile?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitProfile)
            && !explicitProfile.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return Parse(explicitProfile);
        }

        var model = (config.LlmModel ?? string.Empty).ToLowerInvariant();
        var backend = (config.LlmBackend ?? "ollama").Trim().ToLowerInvariant();

        if (ContainsAny(model, "composer", "cursor"))
            return AgenticPromptProfile.ComposerLike;

        if (ContainsAny(model, "qwen", "qwq", "qwen2", "qwen3", "bonsai", "ornith"))
            return AgenticPromptProfile.Qwen;

        if (ContainsAny(model, "claude", "sonnet", "opus", "haiku"))
            return AgenticPromptProfile.Claude;

        if (backend is "openai" or "openai-compatible" or "custom" or "vllm"
            || ContainsAny(model, "gpt", "o1", "o3", "o4", "chatgpt"))
        {
            return AgenticPromptProfile.OpenAi;
        }

        if (backend is "lmstudio" or "lm-studio" or "lm_studio")
        {
            if (ContainsAny(model, "qwen", "qwq", "bonsai", "ornith"))
                return AgenticPromptProfile.Qwen;
            return ContainsAny(model, "claude", "sonnet", "opus", "haiku")
                ? AgenticPromptProfile.Claude
                : AgenticPromptProfile.OpenAi;
        }

        return AgenticPromptProfile.Ollama;
    }

    public static AgenticPromptProfile Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "ollama" or "local" => AgenticPromptProfile.Ollama,
            "openai" or "gpt" => AgenticPromptProfile.OpenAi,
            "claude" or "anthropic" => AgenticPromptProfile.Claude,
            "qwen" => AgenticPromptProfile.Qwen,
            "composer" or "composer-like" or "cursor" => AgenticPromptProfile.ComposerLike,
            _ => AgenticPromptProfile.Ollama
        };

    /// <summary>Default max iterations hint for harness family (caller may still use app config).</summary>
    public static int DefaultMaxIterations(AgenticPromptProfile profile) =>
        profile switch
        {
            AgenticPromptProfile.ComposerLike => 24,
            AgenticPromptProfile.Claude => 16,
            AgenticPromptProfile.Qwen => 12,
            AgenticPromptProfile.OpenAi => 12,
            _ => 10
        };

    public static string ToolCallingHint(AgenticPromptProfile profile) =>
        profile switch
        {
            AgenticPromptProfile.ComposerLike =>
                "Prefer tool_describe → tool call → short observation. Discover context lazily; avoid dumping large payloads into chat.",
            AgenticPromptProfile.Claude =>
                "Use tools via the function-calling interface. Call tool_describe before unfamiliar tools.",
            AgenticPromptProfile.Qwen =>
                "Emit tool/function calls in the backend JSON format. Call tool_describe before first use of unknown tools.",
            AgenticPromptProfile.OpenAi =>
                "Use OpenAI-style function calls. Call tool_describe before first use of unknown tools.",
            _ =>
                "When invoking a tool, emit valid tool/function call JSON for this backend. Call tool_describe before unfamiliar tools."
        };

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
