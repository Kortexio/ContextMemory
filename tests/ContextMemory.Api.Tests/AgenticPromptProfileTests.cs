using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticPromptProfileResolverTests
{
    [Theory]
    [InlineData("ollama", "qwen3.5:9b", "auto", AgenticPromptProfile.Qwen)]
    [InlineData("ollama", "llama3.2", "auto", AgenticPromptProfile.Ollama)]
    [InlineData("openai", "gpt-4o", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("openai", "o3-mini", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("openai", "claude-sonnet-4", "auto", AgenticPromptProfile.Claude)]
    [InlineData("lmstudio", "gpt-4", "auto", AgenticPromptProfile.OpenAi)]
    [InlineData("lmstudio", "claude-3-opus", "auto", AgenticPromptProfile.Claude)]
    [InlineData("ollama", "qwen3.5:9b", "claude", AgenticPromptProfile.Claude)]
    [InlineData("openai", "gpt-4o", "ollama", AgenticPromptProfile.Ollama)]
    [InlineData("openai", "composer-1", "auto", AgenticPromptProfile.ComposerLike)]
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
    public void Build_IncludesToolsAndDefaultSkills()
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
                        IsDefaultEnabled = true,
                        PromptMarkdown = "## Tool-calling discipline\n- Emit tool_calls with valid JSON."
                    }
                ]
            }
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "shell_execute");

        Assert.Contains("shell_execute", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Default skills", prompt, StringComparison.Ordinal);
        Assert.Contains("`tool-calling-discipline`", prompt, StringComparison.Ordinal);
        Assert.Contains("skill_read", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_describe", prompt, StringComparison.OrdinalIgnoreCase);
        // Lazy discovery: skill body is not stuffed into the system prompt.
        Assert.DoesNotContain("Emit tool_calls with valid JSON", prompt, StringComparison.Ordinal);
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
        Assert.DoesNotContain("## Default skills", prompt, StringComparison.Ordinal);
        Assert.Contains("Agentic mode", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InjectsAlwaysOnRules()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "openai",
            LlmModel = "gpt-4o",
            ResolvedPolicy = new ResolvedAgenticPolicy
            {
                ActiveSkills =
                [
                    new AgenticSkillDefinition
                    {
                        Id = "rule-always-evidence",
                        Name = "Evidence first",
                        Activation = AgenticSkillActivation.AlwaysOn,
                        IsDefaultEnabled = true,
                        PromptMarkdown = "Always prefer tools over speculation."
                    }
                ]
            }
        };

        var prompt = AgenticSystemPromptBuilder.Build(config, "shell_execute");
        Assert.Contains("## Always-on rules", prompt, StringComparison.Ordinal);
        Assert.Contains("Always prefer tools over speculation.", prompt, StringComparison.Ordinal);
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

    [Fact]
    public void TruncateWithPointer_LeavesShortPayloadUnchanged()
    {
        var payload = "short ok";
        Assert.Equal(payload, AgenticToolObservationFormatter.TruncateWithPointer("shell", payload, 100));
    }

    [Fact]
    public void TruncateWithPointer_AddsArtifactPointerWhenOverBudget()
    {
        var payload = new string('x', 500);
        var truncated = AgenticToolObservationFormatter.TruncateWithPointer(
            "wiki_search", payload, 120, "tool:wiki_search:deadbeef");
        Assert.True(truncated.Length < payload.Length);
        Assert.Contains("artifactId=tool:wiki_search:deadbeef", truncated);
        Assert.Contains("artifact_tail", truncated);
    }

    [Fact]
    public void Format_TruncatesLongOutput_WithPointer()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "test",
            LlmBackend = "ollama",
            MaxToolObservationChars = 100
        };

        var formatted = AgenticToolObservationFormatter.Format(
            "shell_execute",
            new ToolExecutionResult { Output = new string('y', 400), ExitCode = 0 },
            config,
            "tool:shell_execute:abc");

        Assert.Contains("artifactId=tool:shell_execute:abc", formatted);
        Assert.True(formatted.Length < 400);
    }
}
