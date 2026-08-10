using System.Text;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using ContextMemory.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Mid-turn context compaction (Cursor-style): archive transcript, summarize with WikiLlmModel, shrink messages.
/// </summary>
public interface IAgentContextCompactor
{
    Task<ContextCompactionResult?> TryCompactAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        List<OllamaMessage> messages,
        int iteration,
        CancellationToken cancellationToken = default);
}

public sealed record ContextCompactionResult(
    string HistoryArtifactId,
    string Summary,
    int MessagesBefore,
    int EstimatedTokensBefore);

public sealed class AgentContextCompactor : IAgentContextCompactor
{
    public const string RollingSummaryArtifactId = "meta:rolling_summary";

    private readonly ISessionArtifactStore _artifacts;
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<AgentContextCompactor> _logger;

    public AgentContextCompactor(
        ISessionArtifactStore artifacts,
        ILlmAdapterResolver adapterResolver,
        IOptions<ContextMemoryOptions> options,
        ILogger<AgentContextCompactor> logger)
    {
        _artifacts = artifacts;
        _adapterResolver = adapterResolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ContextCompactionResult?> TryCompactAsync(
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        List<OllamaMessage> messages,
        int iteration,
        CancellationToken cancellationToken = default)
    {
        var maxTokens = SessionWikiSettings.ResolveMaxContextTokens(runtimeConfig, _options);
        var estimated = TokenEstimator.Estimate(messages);
        if (estimated <= maxTokens || messages.Count < 4)
            return null;

        var historyId = $"history:{sessionId}:{iteration}:{Guid.NewGuid():N}"[..64];
        var transcript = SerializeTranscript(messages);

        try
        {
            await _artifacts
                .WriteAsync(appId, userId, sessionId, historyId, transcript, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist history artefact for compaction {AppId}/{SessionId}", appId, sessionId);
            return null;
        }

        var summary = await GenerateSummaryAsync(runtimeConfig, messages, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(summary))
            summary = BuildHeuristicSummary(messages);

        try
        {
            await _artifacts
                .WriteAsync(appId, userId, sessionId, RollingSummaryArtifactId, summary, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist rolling summary after compaction");
        }

        var system = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        var lastUser = messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

        messages.Clear();
        if (system is not null)
            messages.Add(system);
        messages.Add(new OllamaMessage
        {
            Role = "system",
            Content =
                "## Compacted context\n"
                + "Earlier turns were archived. Recover details with artifact_read / session_log_search.\n"
                + $"historyArtifactId={historyId}\n\n"
                + "## Session summary\n"
                + summary.Trim()
        });
        if (lastUser is not null)
            messages.Add(lastUser);

        return new ContextCompactionResult(historyId, summary, estimated, estimated);
    }

    private async Task<string> GenerateSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        List<OllamaMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = SessionWikiSettings.ResolveWikiLlmModel(runtimeConfig, _options.DefaultWikiLlmModel);
            var adapter = _adapterResolver.Resolve(runtimeConfig);
            var prompt =
                "Summarize this agent session for continued work. Max 12 bullet lines. "
                + "Keep goals, decisions, tool outcomes, and open questions. Same language as the user.\n\n"
                + SerializeTranscript(messages.TakeLast(40));

            var response = await adapter.GenerateAsync(
                new OllamaGenerateRequest
                {
                    Model = model,
                    Prompt = prompt,
                    Stream = false
                },
                cancellationToken).ConfigureAwait(false);

            return OllamaLlmText.NormalizeAssistantContent(OllamaLlmText.GetGenerateText(response)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compaction summary LLM failed; using heuristic");
            return string.Empty;
        }
    }

    private static string BuildHeuristicSummary(List<OllamaMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages.TakeLast(8))
        {
            var role = m.Role ?? "?";
            var content = (m.Content ?? string.Empty).Trim();
            if (content.Length > 200)
                content = content[..200] + "…";
            if (string.IsNullOrWhiteSpace(content))
                continue;
            sb.AppendLine($"- {role}: {content}");
        }

        return sb.ToString().Trim();
    }

    private static string SerializeTranscript(IEnumerable<OllamaMessage> messages) =>
        JsonSerializer.Serialize(
            messages.Select(m => new { m.Role, Content = m.Content, ToolCalls = m.ToolCalls?.Count ?? 0 }),
            new JsonSerializerOptions { WriteIndented = true });
}
