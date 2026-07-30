using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticPromptProfileResolverTests
{
    [Theory]
    [InlineData("ollama", "qwen3.5:9b", "auto", AgenticPromptProfile.Ollama)]
    [InlineData("ollama", "llama3.2", "auto", AgenticPromptProfile.Ollama)]
    [InlineData("openai", "gpt-4o", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("openai", "o3-mini", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("openai", "claude-sonnet-4", "auto", AgenticPromptProfile.Claude)]
    [InlineData("lmstudio", "gpt-4", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("lmstudio", "claude-3-opus", "auto", AgenticPromptProfile.Claude)]
    [InlineData("ollama", "qwen3.5:9b", "claude", AgenticPromptProfile.Claude)]
    [InlineData("openai", "gpt-4o", "ollama", AgenticPromptProfile.Ollama)]
    public void Resolve_ReturnsExpectedProfile(
        string backend,
        string model,
        string promptProfile,
        AgenticPromptProfile expected)
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = backend,
            LlmModel = model,
            Agentic = new AgenticConfig { PromptProfile = promptProfile }
        };

        Assert.Equal(expected, AgenticPromptProfileResolver.Resolve(config));
    }
}

public sealed class AgenticSystemPromptBuilderTests
{
    [Fact]
    public void Build_IncludesToolsAndActiveSkills()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "ollama",
            LlmModel = "qwen3.5:9b",
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveSkills =
                [
                    new AgenticSkillDefinition
                    {
                        Id = "tool-calling-discipline",
                        Name = "Tool-calling discipline",
                        PromptMarkdown = "## Tool-calling discipline\n- Emit tool_calls with valid JSON."
                    }
                ]
            }
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "shell_execute");

        Assert.Contains("shell_execute", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_calls", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Active skills", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OpenAi_StillListsTools()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "openai",
            LlmModel = "gpt-4o"
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "shell_execute");

        Assert.Contains("Available tools: shell_execute", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithoutSkills_HasNoActiveSkillsSection()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "openai",
            LlmModel = "claude-sonnet-4"
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "shell_execute");

        Assert.DoesNotContain("## Active skills", prompt, StringComparison.Ordinal);
        Assert.Contains("Agentic mode", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AgenticToolObservationFormatterTests
{
    [Fact]
    public void Format_OpenAiProfile_UsesFunctionWording()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "openai",
            LlmModel = "gpt-4o"
        };

        var formatted = AgenticToolObservationFormatter.Format(
            "shell_execute",
            new ToolExecutionResult { Output = "ok", ExitCode = 0 },
            config);

        Assert.Contains("Function", formatted, StringComparison.OrdinalIgnoreCase);
    }
}
