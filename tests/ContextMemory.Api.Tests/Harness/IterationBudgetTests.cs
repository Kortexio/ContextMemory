using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class IterationBudgetTests
{
    [Fact]
    public void TryRefund_IncreasesEffectiveMax_UntilCap()
    {
        // AbsoluteMax = min(ceiling, base + continueExtensions); refunds grow EffectiveMax inside that headroom.
        var budget = new IterationBudget(
            baseMaxIterations: 5,
            Profile(maxRefunds: 2, maxContinue: 8, absoluteCeiling: 40));
        budget.ResetEffectiveToBase();

        Assert.Equal(5, budget.EffectiveMax);
        // base 5 + continue 8 + refunds 2
        Assert.Equal(15, budget.AbsoluteMax);
        Assert.True(budget.TryRefund());
        Assert.Equal(6, budget.EffectiveMax);
        Assert.True(budget.TryRefund());
        Assert.Equal(7, budget.EffectiveMax);
        Assert.False(budget.TryRefund());
        Assert.Equal(2, budget.RefundsUsed);
        Assert.Equal(7, budget.EffectiveMax);
    }

    [Fact]
    public void TryContinueExtension_ExtendsPastBase_UntilExhausted()
    {
        var budget = new IterationBudget(
            baseMaxIterations: 3,
            Profile(maxRefunds: 0, maxContinue: 2, absoluteCeiling: 40));
        budget.ResetEffectiveToBase();

        Assert.Equal(3, budget.EffectiveMax);
        // base 3 + continue 2 + refunds 0
        Assert.Equal(5, budget.AbsoluteMax);
        Assert.False(budget.TryContinueExtension(currentIterationExclusive: 2));
        Assert.True(budget.TryContinueExtension(currentIterationExclusive: 3));
        Assert.Equal(4, budget.EffectiveMax);
        Assert.True(budget.TryContinueExtension(currentIterationExclusive: 4));
        Assert.Equal(5, budget.EffectiveMax);
        Assert.False(budget.TryContinueExtension(currentIterationExclusive: 5));
        Assert.Equal(2, budget.ContinueExtensions);
        Assert.True(budget.IsExhausted(5));
    }

    [Fact]
    public void AbsoluteCeiling_CapsRefundsAndContinue()
    {
        var budget = new IterationBudget(
            baseMaxIterations: 8,
            Profile(maxRefunds: 20, maxContinue: 20, absoluteCeiling: 10));
        budget.ResetEffectiveToBase();

        Assert.Equal(10, budget.AbsoluteMax);

        while (budget.TryRefund())
        {
            /* drain refunds until AbsoluteMax */
        }

        Assert.Equal(10, budget.EffectiveMax);
        Assert.False(budget.TryContinueExtension(currentIterationExclusive: 10));
        Assert.True(budget.IsExhausted(10));
    }

    private static ModelHarnessProfile Profile(int maxRefunds, int maxContinue, int absoluteCeiling) =>
        new(
            Mode: ModelHarnessMode.Weak,
            MaxRefunds: maxRefunds,
            StallThreshold: 3,
            MaxFinishNudges: 3,
            MaxContinueExtensions: maxContinue,
            AbsoluteMaxIterationsCeiling: absoluteCeiling,
            CompactSchemas: true,
            PreferFullSchemas: true,
            EnableEvidenceFinishGate: true,
            SoftenForceAnswerOnly: true,
            FamilyIds: ModelHarnessProfile.FamilyIdsAll);
}
