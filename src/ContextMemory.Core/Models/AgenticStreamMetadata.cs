using System.Text.Json.Serialization;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Localization;

namespace ContextMemory.Core.Models;

public record AgenticStreamMetadata
{
    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    [JsonPropertyName("iteration")]
    public int? Iteration { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("steps")]
    public IReadOnlyList<AgenticStepSummary>? Steps { get; init; }

    [JsonPropertyName("iterations")]
    public int? Iterations { get; init; }

    [JsonPropertyName("timed_out")]
    public bool? TimedOut { get; init; }

    [JsonPropertyName("max_iterations_reached")]
    public bool? MaxIterationsReached { get; init; }

    [JsonPropertyName("awaiting_confirmation")]
    public bool? AwaitingConfirmation { get; init; }

    [JsonPropertyName("pending_confirmation_id")]
    public string? PendingConfirmationId { get; init; }

    public static AgenticStreamMetadata FromProgress(AgenticProgressEvent evt, string? defaultLanguage = null) =>
        new()
        {
            Phase = evt.Phase.ToString(),
            Iteration = evt.Iteration,
            ToolName = evt.ToolName,
            Detail = evt.Detail,
            Label = AgenticProgressFormatter.FormatEvent(evt, defaultLanguage)
        };

    public static AgenticStreamMetadata FromResult(AgentResult result, string? defaultLanguage = null)
    {
        if (result.AwaitingConfirmation)
        {
            return new AgenticStreamMetadata
            {
                Phase = AgenticProgressPhase.AwaitingConfirmation.ToString(),
                Iterations = result.Iterations,
                AwaitingConfirmation = true,
                PendingConfirmationId = result.PendingConfirmationId,
                Detail = result.FinalAnswer,
                Label = result.PendingKind == AgenticPendingKinds.MaxIterations
                    ? AgenticProgressFormatter.FormatEvent(new AgenticProgressEvent
                    {
                        Phase = AgenticProgressPhase.AwaitingConfirmation,
                        Detail = AgenticMessages.ProgressHumanReviewAfterMaxIterations(defaultLanguage)
                    }, defaultLanguage)
                    : AgenticProgressFormatter.FormatEvent(new AgenticProgressEvent
                    {
                        Phase = AgenticProgressPhase.AwaitingConfirmation,
                        Detail = AgenticMessages.ProgressDestructiveBlocked(defaultLanguage)
                    }, defaultLanguage),
                Steps = result.Steps.Select(s => AgenticStepSummary.FromStep(s, defaultLanguage)).ToList()
            };
        }

        return new AgenticStreamMetadata
        {
            Phase = AgenticProgressPhase.Completed.ToString(),
            Iterations = result.Iterations,
            TimedOut = result.TimedOut ? true : null,
            MaxIterationsReached = result.MaxIterationsReached ? true : null,
            Detail = result.TimedOut
                ? AgenticMessages.ProgressTimedOutDetail(defaultLanguage)
                : result.MaxIterationsReached
                    ? AgenticMessages.ProgressMaxIterationsDetail(defaultLanguage)
                    : AgenticMessages.ProgressCompletedDetail(defaultLanguage),
            Label = result.TimedOut
                ? AgenticProgressFormatter.FormatEvent(new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.TimedOut,
                    Detail = AgenticMessages.ProgressTimedOutWithStats(result.Iterations, defaultLanguage)
                }, defaultLanguage)
                : AgenticProgressFormatter.FormatEvent(new AgenticProgressEvent
                {
                    Phase = AgenticProgressPhase.Completed,
                    Detail = AgenticMessages.ProgressCompletedWithStats(
                        result.Iterations,
                        result.Steps.Count,
                        defaultLanguage)
                }, defaultLanguage),
            Steps = result.Steps.Select(s => AgenticStepSummary.FromStep(s, defaultLanguage)).ToList()
        };
    }
}

public record AgenticStepSummary
{
    [JsonPropertyName("iteration")]
    public int Iteration { get; init; }

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    public static AgenticStepSummary FromStep(AgentExecutionStep step, string? defaultLanguage = null) =>
        new()
        {
            Iteration = step.Iteration,
            ToolName = step.ToolName,
            Success = step.Success,
            ExitCode = step.ExitCode,
            DurationMs = step.Duration.TotalMilliseconds,
            Label = AgenticProgressFormatter.FormatToolCompleted(step, defaultLanguage)
        };
}
