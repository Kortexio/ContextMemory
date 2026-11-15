namespace ContextMemory.Core.Agentic.Harness.Families;

public sealed record LiftedToolCall(string Id, string Name, string ArgumentsJson);

public sealed record GrammarParseContext(IReadOnlySet<string> ToolNames);

public sealed record GrammarParseResult(IReadOnlyList<LiftedToolCall> Calls, string CleanedText);

public interface IGrammarParser
{
    string Id { get; }
    bool Detect(string text);
    GrammarParseResult Parse(string text, GrammarParseContext ctx);
}

public sealed record FamilyRepairEvent(
    string Family,
    string Mode,
    int RecoveredCalls,
    bool FromReasoning);

public sealed record FamilyRecoveryResult(
    IReadOnlyList<LiftedToolCall> Calls,
    string CleanedText,
    string? CleanedReasoning,
    FamilyRepairEvent? Event);
