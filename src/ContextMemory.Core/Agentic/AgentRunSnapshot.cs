using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Snapshot that allows resuming an agent run after pause / HITL / process restart (CM-4).
/// </summary>
public sealed class AgentRunSnapshot
{
    public required string RunId { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public AgentLoopState LoopState { get; init; } = AgentLoopState.Created;
    public AgentRunState RunState { get; init; } = AgentRunState.Created;
    public int Iteration { get; init; }
    public string? TraceId { get; init; }
    public string? LastAnswer { get; init; }
    public List<OllamaMessage> Messages { get; init; } = [];
    public List<AgentExecutionStep> Steps { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string ArtifactId(string runId) => $"meta:agent_run:{runId}";
}

public interface IAgentRunStore
{
    Task SaveAsync(AgentRunSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<AgentRunSnapshot?> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        string runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists agent run snapshots as session artifacts.
/// </summary>
public sealed class ArtifactAgentRunStore : IAgentRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ISessionArtifactStore _artifacts;

    public ArtifactAgentRunStore(ISessionArtifactStore artifacts) => _artifacts = artifacts;

    public async Task SaveAsync(AgentRunSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _artifacts
            .WriteAsync(
                snapshot.AppId,
                snapshot.UserId,
                snapshot.SessionId,
                AgentRunSnapshot.ArtifactId(snapshot.RunId),
                json,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentRunSnapshot?> LoadAsync(
        string appId,
        string userId,
        string sessionId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var json = await _artifacts
            .ReadAsync(appId, userId, sessionId, AgentRunSnapshot.ArtifactId(runId), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<AgentRunSnapshot>(json, JsonOptions);
    }
}
