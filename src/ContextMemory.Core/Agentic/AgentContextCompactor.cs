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

        // Keep under common FS/DB id limits without assuming the prefix is already >= 64 chars
        // (short session ids like Jira keys produced ArgumentOutOfRange on ..[64]).
        var historyId = $"history:{sessionId}:{iteration}:{Guid.NewGuid():N}";
        if (historyId.Length > 64)
            historyId = historyId[..64];
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
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
            && !IsToolResponseWrapped(m.Content));

        // Qwen/Bonsai chat templates raise if a second role=system appears.
        // Merge compaction into the single leading system message.
        var compactionBlock =
            "## Compacted context\n"
            + "Earlier turns were archived. Recover details with artifact_read / session_log_search.\n"
            + $"historyArtifactId={historyId}\n\n"
            + "## Session summary\n"
            + summary.Trim();

        var mergedSystemContent = string.IsNullOrWhiteSpace(system?.Content)
            ? compactionBlock
            : system!.Content.TrimEnd() + "\n\n" + compactionBlock;

        messages.Clear();
        messages.Add(new OllamaMessage
        {
            Role = "system",
            Content = mergedSystemContent
        });
        if (lastUser is not null)
            messages.Add(lastUser);
        else
        {
            // Strict templates (Qwen3.5 multi_step_tool) require a real user query.
            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = "Continue from the compacted session summary. Prefer tools for live data."
            });
        }

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

    private static bool IsToolResponseWrapped(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var trimmed = content.Trim();
        return trimmed.StartsWith("<tool_response>", StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith("</tool_response>", StringComparison.OrdinalIgnoreCase);
    }
}
