using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Engine;

/// <summary>
/// Proportional character-budget allocator across prompt tiers (CM-2).
/// </summary>
public sealed class ContextBudgetAllocator : IContextBudgetAllocator
{
    public ContextBudgetAllocation Allocate(int totalMaxChars, ContextBudgetWeights? weights = null)
    {
        var total = Math.Max(0, totalMaxChars);
        var w = weights ?? new ContextBudgetWeights();

        var systemW = Math.Max(0, w.SystemPrompt);
        var wikiW = Math.Max(0, w.Wiki);
        var digestsW = Math.Max(0, w.Digests);
        var historyW = Math.Max(0, w.History);
        var workingW = Math.Max(0, w.WorkingMemory);
        var sum = systemW + wikiW + digestsW + historyW + workingW;

        if (total == 0 || sum <= 0)
        {
            return new ContextBudgetAllocation(0, 0, 0, 0, 0, total);
        }

        // Floor each tier, then distribute leftover chars by fractional remainder.
        var raw = new[]
        {
            total * systemW / sum,
            total * wikiW / sum,
            total * digestsW / sum,
            total * historyW / sum,
            total * workingW / sum
        };

        var floors = new int[5];
        var remainders = new (int Index, double Fraction)[5];
        var allocated = 0;
        for (var i = 0; i < 5; i++)
        {
            floors[i] = (int)Math.Floor(raw[i]);
            remainders[i] = (i, raw[i] - floors[i]);
            allocated += floors[i];
        }

        var leftover = total - allocated;
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Fraction))
        {
            if (leftover <= 0)
                break;
            floors[index]++;
            leftover--;
        }

        return new ContextBudgetAllocation(
            SystemPromptChars: floors[0],
            WikiChars: floors[1],
            DigestsChars: floors[2],
            HistoryChars: floors[3],
            WorkingMemoryChars: floors[4],
            TotalMaxChars: total);
    }
}
