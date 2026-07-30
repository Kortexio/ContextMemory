using System.Text.Json.Serialization;

namespace ContextMemory.Core.Agentic;

public record PolicyPacksConfig
{
    [JsonPropertyName("enabledSkillIds")]
    public List<string>? EnabledSkillIds { get; init; }

    [JsonPropertyName("enabledGuardrailIds")]
    public List<string>? EnabledGuardrailIds { get; init; }

    /// <summary>True when the app explicitly set skill/guardrail lists (even if empty).</summary>
    [JsonIgnore]
    public bool IsExplicit => EnabledSkillIds is not null || EnabledGuardrailIds is not null;
}

public sealed record AgenticSkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string PromptMarkdown { get; init; } = string.Empty;
    public string Category { get; init; } = "general";
    public bool IsSystem { get; init; }
    public bool IsDefaultEnabled { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<string> LinkedGuardrailIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AgenticGuardrailDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Kind { get; init; }
    public string ConfigJson { get; init; } = "{}";
    public bool IsSystem { get; init; }
    public bool IsDefaultEnabled { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AgenticCatalogSnapshot
{
    public IReadOnlyList<AgenticSkillDefinition> Skills { get; init; } = [];
    public IReadOnlyList<AgenticGuardrailDefinition> Guardrails { get; init; } = [];
}

public sealed record ResolvedAgenticPolicy
{
    public IReadOnlyList<AgenticSkillDefinition> ActiveSkills { get; init; } = [];
    public IReadOnlyList<AgenticGuardrailDefinition> ActiveGuardrails { get; init; } = [];
    public IReadOnlySet<string> ActiveGuardrailKinds { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasKind(string kind) =>
        ActiveGuardrailKinds.Contains(kind);

    public AgenticGuardrailDefinition? FindByKind(string kind) =>
        ActiveGuardrails.FirstOrDefault(g =>
            string.Equals(g.Kind, kind, StringComparison.OrdinalIgnoreCase));
}

public static class AgenticGuardrailKinds
{
    public const string UrlFetch = "url-fetch";
    public const string SandboxClaim = "sandbox-claim";
    public const string ToolFailureDisclosure = "tool-failure-disclosure";
    public const string BlockedPatterns = "blocked-patterns";
}
