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
        CancellationToken cancellationToken = default,
        WorkingMemory? workingMemory = null,
        int? tokenBudgetOverride = null,
        bool force = false);
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
    private readonly ILlmModelRouter _modelRouter;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<AgentContextCompactor> _logger;

    public AgentContextCompactor(
        ISessionArtifactStore artifacts,
        ILlmAdapterResolver adapterResolver,
        ILlmModelRouter modelRouter,
        IOptions<ContextMemoryOptions> options,
        ILogger<AgentContextCompactor> logger)
    {
        _artifacts = artifacts;
        _adapterResolver = adapterResolver;
        _modelRouter = modelRouter;
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
        CancellationToken cancellationToken = default,
        WorkingMemory? workingMemory = null,
        int? tokenBudgetOverride = null,
        bool force = false)
    {
        var maxTokens = tokenBudgetOverride is > 0
            ? tokenBudgetOverride.Value
            : SessionWikiSettings.ResolveAgentCompactionTokenBudget(runtimeConfig, _options, null);
        var estimated = TokenEstimator.Estimate(messages);
        if (!force && (estimated <= maxTokens || messages.Count < 4))
            return null;

        if (messages.Count < 4)
        {
            ShrinkToBudget(messages, maxTokens);
            return new ContextCompactionResult("inline", string.Empty, estimated, estimated);
        }

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

        var summary = await GenerateSummaryAsync(runtimeConfig, messages, workingMemory, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(summary))
            summary = BuildHeuristicSummary(messages, workingMemory);

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

        ShrinkToBudget(messages, maxTokens);
        return new ContextCompactionResult(historyId, summary, estimated, estimated);
    }

    /// <summary>
    /// Collapse to system + last user and truncate so the estimated prompt fits <paramref name="maxTokens"/>.
    /// No-op when already under budget.
    /// </summary>
    public static void ShrinkToBudget(List<OllamaMessage> messages, int maxTokens)
    {
        if (messages.Count == 0)
            return;

        maxTokens = Math.Max(64, maxTokens);
        if (TokenEstimator.Estimate(messages) <= maxTokens)
            return;

        var system = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        var lastUser = messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
            && !IsToolResponseWrapped(m.Content));

        var systemContent = system?.Content ?? string.Empty;
        var userContent = string.IsNullOrWhiteSpace(lastUser?.Content)
            ? "Continue from the compacted session summary. Prefer tools for live data."
            : lastUser!.Content;

        messages.Clear();
        messages.Add(new OllamaMessage { Role = "system", Content = systemContent });
        messages.Add(new OllamaMessage { Role = "user", Content = userContent });

        if (TokenEstimator.Estimate(messages) <= maxTokens)
            return;

        var userTokens = TokenEstimator.Estimate(userContent);
        var systemTokenBudget = Math.Max(32, maxTokens - userTokens - 8);
        var truncatedSystem = TruncatePreservingHeadAndTail(systemContent, systemTokenBudget * 4);
        messages[0] = messages[0] with { Content = truncatedSystem };

        if (TokenEstimator.Estimate(messages) <= maxTokens)
            return;

        var remainingUserTokens = Math.Max(16, maxTokens - TokenEstimator.Estimate(messages[0].Content) - 4);
        var userChars = remainingUserTokens * 4;
        if (userContent.Length > userChars)
            messages[1] = messages[1] with { Content = userContent[..userChars] + "…" };
    }

    private static string TruncatePreservingHeadAndTail(string content, int maxChars)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxChars)
            return content;

        maxChars = Math.Max(80, maxChars);
        const string marker = "\n\n[…truncated for context window…]\n\n";
        var keepHead = Math.Max(40, maxChars * 2 / 3);
        var keepTail = Math.Max(0, maxChars - keepHead - marker.Length);
        if (keepTail < 40)
            return content[..Math.Min(content.Length, maxChars)] + "…";

        return content[..keepHead] + marker + content[^keepTail..];
    }

    private async Task<string> GenerateSummaryAsync(
        AppRuntimeConfig runtimeConfig,
        List<OllamaMessage> messages,
        WorkingMemory? workingMemory,
        CancellationToken cancellationToken)
    {
        try
        {
            var estimatedTokens = TokenEstimator.Estimate(messages.TakeLast(40));
            var routed = _modelRouter.Route(new ModelRoutingRequest(
                LlmTaskType.Compaction,
                runtimeConfig,
                RequiresVision: false,
                EstimatedTokens: estimatedTokens));
            var model = routed.Model;
            var adapter = _adapterResolver.Resolve(runtimeConfig);
            var importanceNote = BuildImportancePreservationNote(workingMemory);
            var prompt =
                "Summarize this agent session for continued work. Max 12 bullet lines. "
                + "Keep goals, decisions, tool outcomes, and open questions. Same language as the user. "
                + "Preserve Critical and Important items verbatim when possible; "
                + "Recoverable items may become artifact pointers; Discardable items can be dropped.\n"
                + importanceNote
                + "\n"
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

    private static string BuildHeuristicSummary(List<OllamaMessage> messages, WorkingMemory? workingMemory = null)
    {
        var sb = new StringBuilder();
        var note = BuildImportancePreservationNote(workingMemory);
        if (!string.IsNullOrWhiteSpace(note))
            sb.AppendLine(note.Trim());

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

    private static string BuildImportancePreservationNote(WorkingMemory? workingMemory)
    {
        if (workingMemory is null)
            return "Prefer preserving Critical/Important content over Discardable noise.\n";

        var preserved = workingMemory.Items
            .Where(i => i.IsPreservedOnCompaction)
            .OrderBy(i => i.Importance)
            .Take(8)
            .Select(i => $"- [{i.Importance}] {i.Key}: {Truncate(i.Value, 120)}")
            .ToList();

        if (preserved.Count == 0
            && string.IsNullOrWhiteSpace(workingMemory.Objective)
            && string.IsNullOrWhiteSpace(workingMemory.Plan))
        {
            return "Prefer preserving Critical/Important content over Discardable noise.\n";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Critical/Important items to preserve in the summary:");
        if (!string.IsNullOrWhiteSpace(workingMemory.Objective))
            sb.AppendLine($"- [Critical] Objective: {workingMemory.Objective.Trim()}");
        if (!string.IsNullOrWhiteSpace(workingMemory.Plan))
            sb.AppendLine($"- [Important] Plan: {workingMemory.Plan.Trim()}");
        foreach (var line in preserved)
            sb.AppendLine(line);
        if (workingMemory.Blockers.Count > 0)
            sb.AppendLine($"- [Important] Blockers: {string.Join("; ", workingMemory.Blockers.Take(5))}");
        return sb.ToString();
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value
        : value[..max] + "…";

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
