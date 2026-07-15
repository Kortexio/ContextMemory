using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public enum AgenticPromptProfile
{
    Ollama,
    OpenAi,
    Claude
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

        if (ContainsAny(model, "claude", "sonnet", "opus", "haiku"))
            return AgenticPromptProfile.Claude;

        if (backend is "openai"
            || ContainsAny(model, "gpt", "o1", "o3", "o4", "chatgpt"))
        {
            return AgenticPromptProfile.OpenAi;
        }

        if (backend is "lmstudio" or "lm-studio" or "lm_studio")
        {
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
            _ => AgenticPromptProfile.Ollama
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
