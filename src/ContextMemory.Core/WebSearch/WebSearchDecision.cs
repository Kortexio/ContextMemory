namespace ContextMemory.Core.WebSearch;

public sealed record WebSearchDecision(bool ShouldSearch, string? Query, string? SkipReason, string? Source = null)
{
    public static WebSearchDecision Search(string query, string source) =>
        new(true, query, null, source);

    public static WebSearchDecision Skip(string reason, string? source = null) =>
        new(false, null, reason, source);
}
