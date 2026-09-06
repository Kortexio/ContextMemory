namespace ContextMemory.Core.Agentic;

/// <summary>
/// Turn-by-turn audit trail for an agent run (CM-1 Trace Model).
/// </summary>
public sealed class AgentTrace
{
    public required string TraceId { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public AgentRunState RunState { get; set; } = AgentRunState.Created;
    public AgentLoopState LoopState { get; set; } = AgentLoopState.Created;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Model { get; set; }
    public List<AgentTraceTurn> Turns { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static AgentTrace Start(string appId, string userId, string sessionId, string? model = null) =>
        new()
        {
            TraceId = Guid.NewGuid().ToString("N"),
            AppId = appId,
            UserId = userId,
            SessionId = sessionId,
            Model = model,
            RunState = AgentRunState.Running,
            LoopState = AgentLoopState.Executing
        };

    public AgentTraceTurn BeginTurn(int iteration, AgentLoopState state)
    {
        var turn = new AgentTraceTurn
        {
            TurnId = Guid.NewGuid().ToString("N")[..12],
            Iteration = iteration,
            State = state,
            StartedAt = DateTimeOffset.UtcNow
        };
        Turns.Add(turn);
        LoopState = state;
        return turn;
    }

    public void Complete(AgentRunState finalState = AgentRunState.Completed)
    {
        RunState = finalState;
        LoopState = finalState switch
        {
            AgentRunState.Completed => AgentLoopState.Completed,
            AgentRunState.Failed => AgentLoopState.Failed,
            AgentRunState.Cancelled => AgentLoopState.Cancelled,
            AgentRunState.AwaitingHuman => AgentLoopState.WaitingForHuman,
            _ => LoopState
        };
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class AgentTraceTurn
{
    public required string TurnId { get; init; }
    public required int Iteration { get; init; }
    public AgentLoopState State { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? InputPreview { get; set; }
    public string? ContextSnapshotSummary { get; set; }
    public string? Model { get; set; }
    public string? ToolName { get; set; }
    public string? ToolRequestPreview { get; set; }
    public string? ToolResultPreview { get; set; }
    public string? PolicyDecision { get; set; }
    public string? ValidationOutcome { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public long LatencyMs { get; set; }
    public string? Outcome { get; set; }
    public string? Error { get; set; }

    public void Complete(string? outcome = null, string? error = null)
    {
        CompletedAt = DateTimeOffset.UtcNow;
        LatencyMs = (long)(CompletedAt.Value - StartedAt).TotalMilliseconds;
        Outcome = outcome;
        Error = error;
    }
}
