namespace ContextMemory.Core.Agentic;

public enum ShortMcpNameKind
{
    Exact,
    Unique,
    Ambiguous,
    NotFound,
    AlreadyQualified
}

public readonly record struct ShortMcpNameResolution(
    ShortMcpNameKind Kind,
    string? ResolvedName,
    IReadOnlyList<string> Candidates);
