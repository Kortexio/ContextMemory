using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Mcp;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticRequiredArgumentsGuardTests
{
    [Fact]
    public void Rejects_ShellExecute_WithEmptyArgs()
    {
        var catalog = new List<OllamaTool>
        {
            ShellTool()
        };

        var rejected = AgenticRequiredArgumentsGuard.TryReject(
            "shell_execute",
            "{}",
            catalog,
            Config(),
            out var feedback);

        Assert.True(rejected);
        Assert.Contains("command", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shell_execute", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_ShellExecute_WithCommand()
    {
        var catalog = new List<OllamaTool> { ShellTool() };

        var rejected = AgenticRequiredArgumentsGuard.TryReject(
            "shell_execute",
            """{"command":"echo hi"}""",
            catalog,
            Config(),
            out _);

        Assert.False(rejected);
    }

    [Fact]
    public void Ignores_OpenStubMcpTool()
    {
        var catalog = new List<OllamaTool>
        {
            new(
                "function",
                new OllamaFunction(
                    "zuora__query_objects",
                    "query",
                    McpPinnedToolFactory.OpenStubParameters()))
        };

        var rejected = AgenticRequiredArgumentsGuard.TryReject(
            "zuora__query_objects",
            "{}",
            catalog,
            Config(),
            out _);

        Assert.False(rejected);
    }

    [Fact]
    public void Accepts_CaseInsensitiveRequiredField()
    {
        var catalog = new List<OllamaTool> { ShellTool() };

        var rejected = AgenticRequiredArgumentsGuard.TryReject(
            "shell_execute",
            """{"Command":"ls"}""",
            catalog,
            Config(),
            out _);

        Assert.False(rejected);
    }

    private static OllamaTool ShellTool() =>
        new(
            "function",
            new OllamaFunction(
                "shell_execute",
                "Run a shell command",
                new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["command"] = new Dictionary<string, object?> { ["type"] = "string" }
                    },
                    ["required"] = new[] { "command" }
                }));

    private static AppRuntimeConfig Config() =>
        new()
        {
            AppId = "test",
            DefaultLanguage = "pt"
        };
}
