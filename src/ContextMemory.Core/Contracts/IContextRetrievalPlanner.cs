namespace ContextMemory.Core.Contracts;

/// <summary>
/// Decides how context should be retrieved for a query (CM-2).
/// </summary>
public interface IContextRetrievalPlanner
{
    ContextRetrievalPlan Plan(string? query, ContextRetrievalPlannerInput? input = null);
}

/// <summary>
/// Optional hints for retrieval planning.
/// </summary>
public sealed record ContextRetrievalPlannerInput(
    int? AvailableBudgetChars = null,
    bool HasSessionWiki = false,
    bool HasGlobalDigests = false,
    bool HasLargeArtifacts = false,
    bool AgenticEnabled = true,
    int? EstimatedPayloadChars = null);

/// <summary>
/// How a piece of context should enter the agent working set.
/// </summary>
public enum ContextRetrievalMode
{
    /// <summary>Inject compact material directly into the system / user prompt.</summary>
    StaticInject = 0,

    /// <summary>Discover on demand via tools (wiki_search, session_log_search, etc.).</summary>
    LazyDiscovery = 1,

    /// <summary>Keep a pointer (artifactId) and hydrate with artifact_read / artifact_tail.</summary>
    ArtifactReference = 2
}

/// <summary>
/// One recommended retrieval action.
/// </summary>
public sealed record ContextRetrievalAction(
    ContextRetrievalMode Mode,
    string Reason,
    string? Target = null);

/// <summary>
/// Full retrieval plan for a turn.
/// </summary>
public sealed record ContextRetrievalPlan(
    ContextRetrievalMode PrimaryMode,
    IReadOnlyList<ContextRetrievalAction> Actions,
    string Rationale);
