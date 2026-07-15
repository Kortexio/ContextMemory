using ContextMemory.Core.Models;

namespace ContextMemory.Core.Session;

public sealed class SessionSnapshot
{
    public required string SessionPath { get; init; }
    public string IndexMd { get; init; } = string.Empty;
    public string LogMd { get; init; } = string.Empty;
    public string SchemaMd { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Pages { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, DateTimeOffset> PageLastModified { get; init; } =
        new Dictionary<string, DateTimeOffset>();
    public IReadOnlyList<OllamaMessage> Messages { get; init; } = [];
}

public sealed class SessionWikiUpdate
{
    public string? LogEntry { get; init; }
    public string? IndexMd { get; init; }
    public IReadOnlyList<SessionPageUpdate> Pages { get; init; } = [];
    public IReadOnlyList<string> DeletePages { get; init; } = [];
}

public sealed class SessionPageUpdate
{
    public required string Path { get; init; }
    public required string Content { get; init; }
}
