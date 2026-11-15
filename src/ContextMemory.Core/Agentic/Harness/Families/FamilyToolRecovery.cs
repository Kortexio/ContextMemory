namespace ContextMemory.Core.Agentic.Harness.Families;

/// <summary>
/// Orchestrates per-family grammar parsers: tries families in order on text then reasoning,
/// stops at the first family that recovers calls, and never promotes when native tool_calls
/// already arrived for the turn.
/// </summary>
public static class FamilyToolRecovery
{
    public static FamilyRecoveryResult Recover(
        string text,
        string? reasoningText,
        IReadOnlySet<string> toolNames,
        IReadOnlyList<string> familyIds,
        bool hasNativeToolCalls,
        string mode = "recover")
    {
        if (hasNativeToolCalls)
            return new FamilyRecoveryResult(Array.Empty<LiftedToolCall>(), text, reasoningText, null);

        var ctx = new GrammarParseContext(toolNames);
        var cleanedText = text;
        var cleanedReasoning = reasoningText;
        FamilyRepairEvent? evt = null;
        IReadOnlyList<LiftedToolCall> calls = Array.Empty<LiftedToolCall>();

        foreach (var id in familyIds)
        {
            var parser = FamilyRegistry.Get(id);
            if (parser is null)
                continue;

            var matchesText = parser.Detect(cleanedText);
            var matchesReasoning = cleanedReasoning is not null && parser.Detect(cleanedReasoning);
            if (!matchesText && !matchesReasoning)
                continue;

            var found = new List<LiftedToolCall>();

            if (matchesText)
            {
                var result = parser.Parse(cleanedText, ctx);
                if (result.Calls.Count > 0)
                {
                    found.AddRange(result.Calls);
                    cleanedText = result.CleanedText;
                }
            }

            if (matchesReasoning && cleanedReasoning is not null)
            {
                var result = parser.Parse(cleanedReasoning, ctx);
                if (result.Calls.Count > 0)
                {
                    found.AddRange(result.Calls);
                    cleanedReasoning = result.CleanedText;
                }
            }

            if (found.Count == 0)
                continue;

            var isRecover = string.Equals(mode, "recover", StringComparison.OrdinalIgnoreCase);
            evt = new FamilyRepairEvent(
                Family: id,
                Mode: mode,
                RecoveredCalls: isRecover ? found.Count : 0,
                FromReasoning: matchesReasoning && !matchesText);

            if (isRecover)
                calls = found;

            break;
        }

        return new FamilyRecoveryResult(
            FamilySafety.DedupeIdentical(calls),
            cleanedText,
            cleanedReasoning,
            evt);
    }
}
