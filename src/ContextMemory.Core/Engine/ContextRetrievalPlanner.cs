using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Engine;

/// <summary>
/// Heuristic planner: StaticInject vs LazyDiscovery vs ArtifactReference (CM-2).
/// </summary>
public sealed class ContextRetrievalPlanner : IContextRetrievalPlanner
{
    private static readonly string[] ArtifactHints =
    [
        "artifact", "artifactid", "artifact_read", "artifact_tail", "history:", "meta:rolling",
        "log output", "stack trace", "transcript", "large output", "tool output"
    ];

    private static readonly string[] LazyHints =
    [
        "search", "find", "lookup", "where", "which document", "wiki", "grep", "full document",
        "details", "explain how", "compare", "list all", "documentation", "confluence", "jira"
    ];

    private static readonly string[] StaticHints =
    [
        "hello", "hi", "thanks", "obrigado", "olá", "resumo", "summary", "status", "continue",
        "what did we decide", "objetivo", "objective"
    ];

    public ContextRetrievalPlan Plan(string? query, ContextRetrievalPlannerInput? input = null)
    {
        input ??= new ContextRetrievalPlannerInput();
        var q = (query ?? string.Empty).Trim();
        var lower = q.ToLowerInvariant();
        var actions = new List<ContextRetrievalAction>();

        var looksLikeArtifact = ContainsAny(lower, ArtifactHints) || input.HasLargeArtifacts
            || (input.EstimatedPayloadChars is > 4_000);
        var looksLikeLazy = ContainsAny(lower, LazyHints)
            || (input.AvailableBudgetChars is < 1_500 && q.Length > 80);
        var looksLikeStatic = q.Length == 0 || q.Length < 40 || ContainsAny(lower, StaticHints);

        if (looksLikeArtifact)
        {
            actions.Add(new ContextRetrievalAction(
                ContextRetrievalMode.ArtifactReference,
                "Query or payload suggests large recoverable material; keep artifact pointers.",
                Target: "artifact_read|artifact_tail"));
        }

        if (looksLikeLazy && input.AgenticEnabled)
        {
            actions.Add(new ContextRetrievalAction(
                ContextRetrievalMode.LazyDiscovery,
                "Query needs on-demand discovery rather than stuffing full bodies.",
                Target: "wiki_search|session_log_search|wiki_grep"));
        }

        // Always allow a lean static inject when wiki/digests exist (or for short turns).
        if (input.HasSessionWiki || input.HasGlobalDigests || looksLikeStatic || actions.Count == 0)
        {
            actions.Add(new ContextRetrievalAction(
                ContextRetrievalMode.StaticInject,
                "Inject budgeted wiki/digests/working-memory into the prompt.",
                Target: "system_prompt"));
        }

        var primary = looksLikeArtifact
            ? ContextRetrievalMode.ArtifactReference
            : looksLikeLazy && input.AgenticEnabled
                ? ContextRetrievalMode.LazyDiscovery
                : ContextRetrievalMode.StaticInject;

        // Ensure primary action is first.
        actions = actions
            .OrderBy(a => a.Mode == primary ? 0 : 1)
            .ThenBy(a => (int)a.Mode)
            .DistinctBy(a => a.Mode)
            .ToList();

        var rationale = primary switch
        {
            ContextRetrievalMode.ArtifactReference =>
                "Prefer artifact references so large outputs stay out of the prompt.",
            ContextRetrievalMode.LazyDiscovery =>
                "Prefer tool-based discovery; inject only digests/index as seeds.",
            _ =>
                "Prefer static inject of budgeted working set for this turn."
        };

        return new ContextRetrievalPlan(primary, actions, rationale);
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
