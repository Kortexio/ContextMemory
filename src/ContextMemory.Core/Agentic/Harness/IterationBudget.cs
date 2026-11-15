namespace ContextMemory.Core.Agentic.Harness;

/// <summary>
/// Max-iterations with refunds and Continue extensions (CM analogue of CodeForge step refund / Continue).
/// Absolute ceiling prevents infinite cost loops; HITL only after Continue is exhausted.
/// </summary>
public sealed class IterationBudget
{
    private readonly int _baseMax;
    private readonly int _absoluteMax;
    private readonly int _maxRefunds;
    private readonly int _maxContinueExtensions;

    public IterationBudget(int baseMaxIterations, ModelHarnessProfile profile)
    {
        _baseMax = Math.Max(1, baseMaxIterations);
        _maxRefunds = Math.Max(0, profile.MaxRefunds);
        _maxContinueExtensions = Math.Max(0, profile.MaxContinueExtensions);
        // Headroom must cover both Continue extensions and refunds (same pool under the ceiling).
        _absoluteMax = Math.Min(
            profile.AbsoluteMaxIterationsCeiling,
            _baseMax + _maxContinueExtensions + _maxRefunds);
        if (_absoluteMax < _baseMax)
            _absoluteMax = _baseMax;
    }

    public int BaseMax => _baseMax;
    public int AbsoluteMax => _absoluteMax;
    public int EffectiveMax { get; private set; }
    public int RefundsUsed { get; private set; }
    public int ContinueExtensions { get; private set; }

    public void ResetEffectiveToBase() => EffectiveMax = _baseMax;

    /// <summary>Refund one iteration slot (schema repair, overflow, recoverable reject). Returns false if cap hit.</summary>
    public bool TryRefund()
    {
        if (RefundsUsed >= _maxRefunds)
            return false;
        RefundsUsed++;
        EffectiveMax = Math.Min(_absoluteMax, EffectiveMax + 1);
        return true;
    }

    /// <summary>
    /// When the loop hits EffectiveMax, extend once more (Continue) up to AbsoluteMax.
    /// Returns false when no further extension is possible.
    /// </summary>
    public bool TryContinueExtension(int currentIterationExclusive)
    {
        if (ContinueExtensions >= _maxContinueExtensions)
            return false;
        if (EffectiveMax >= _absoluteMax)
            return false;
        if (currentIterationExclusive < EffectiveMax)
            return false;

        ContinueExtensions++;
        EffectiveMax = Math.Min(_absoluteMax, EffectiveMax + 1);
        return true;
    }

    public bool IsExhausted(int iterationExclusive) =>
        iterationExclusive >= EffectiveMax && !CanExtendFurther();

    public bool CanExtendFurther() =>
        ContinueExtensions < _maxContinueExtensions && EffectiveMax < _absoluteMax;
}
