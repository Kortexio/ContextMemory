using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class AgenticProgressFormatterTests
{
    [Fact]
    public void FormatEvent_ToolCompleted_IncludesStatus()
    {
        var label = AgenticProgressFormatter.FormatEvent(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.ToolCompleted,
            Step = new AgentExecutionStep
            {
                Iteration = 1,
                ToolName = "shell_execute",
                Arguments = "{}",
                Output = "ok",
                ExitCode = 0,
                Success = true,
                Duration = TimeSpan.FromMilliseconds(42)
            }
        });

        Assert.Contains("shell_execute", label);
        Assert.Contains("OK", label);
        Assert.Contains("42", label);
    }

    [Fact]
    public void FromProgress_IncludesReadableLabel()
    {
        var meta = AgenticStreamMetadata.FromProgress(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.LlmRequest,
            Iteration = 2
        });

        Assert.Equal("LlmRequest", meta.Phase);
        Assert.Contains("Iteration 2", meta.Label);
    }

    [Fact]
    public void ToProgressChunk_HasNoAssistantContent()
    {
        var chunk = AgenticProgressChunkMapper.ToProgressChunk(
            "test-model",
            new AgenticProgressEvent { Phase = AgenticProgressPhase.Started });

        Assert.Null(chunk.Message);
        Assert.NotNull(chunk.ContextMemory?.Agentic);
        Assert.Equal("Started", chunk.ContextMemory.Agentic.Phase);
    }

    [Fact]
    public void FromResult_AwaitingConfirmation_ExposesPendingIdAndShortLabel()
    {
        var meta = AgenticStreamMetadata.FromResult(
            AgentResult.AwaitingHumanConfirmation(
                "⚠️ **Human confirmation required** to run `shell_execute`.\n\n"
                + "Reply **confirm** or send `[CONFIRM:abc123]` to authorize.",
                "abc123",
                [],
                1));

        Assert.Equal("AwaitingConfirmation", meta.Phase);
        Assert.True(meta.AwaitingConfirmation);
        Assert.Equal("abc123", meta.PendingConfirmationId);
        Assert.Contains("human confirmation", meta.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[CONFIRM:abc123]", meta.Detail);
    }

    [Fact]
    public void FromProgress_AwaitingConfirmation_UsesDetailWhenPresent()
    {
        var meta = AgenticStreamMetadata.FromProgress(new AgenticProgressEvent
        {
            Phase = AgenticProgressPhase.AwaitingConfirmation,
            ToolName = "shell_execute",
            Detail = "Awaiting confirmation for `shell_execute`."
        });

        Assert.Equal("AwaitingConfirmation", meta.Phase);
        Assert.Contains("shell_execute", meta.Label);
    }
}
