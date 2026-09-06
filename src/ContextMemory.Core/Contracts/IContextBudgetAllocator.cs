namespace ContextMemory.Core.Contracts;

/// <summary>
/// Allocates a total character budget across context tiers (CM-2).
/// </summary>
public interface IContextBudgetAllocator
{
    ContextBudgetAllocation Allocate(int totalMaxChars, ContextBudgetWeights? weights = null);
}

/// <summary>
/// Relative weights for each context tier. Values are normalized before allocation.
/// </summary>
public sealed record ContextBudgetWeights(
    double SystemPrompt = 0.15,
    double Wiki = 0.35,
    double Digests = 0.15,
    double History = 0.20,
    double WorkingMemory = 0.15);

/// <summary>
/// Character budgets per context tier.
/// </summary>
public sealed record ContextBudgetAllocation(
    int SystemPromptChars,
    int WikiChars,
    int DigestsChars,
    int HistoryChars,
    int WorkingMemoryChars,
    int TotalMaxChars);
