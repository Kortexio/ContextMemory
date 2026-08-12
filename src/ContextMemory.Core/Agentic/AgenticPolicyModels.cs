using System.Text.Json.Serialization;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Legacy per-app selection of platform pack IDs. Ignored by the resolver (platform uses
/// <see cref="AgenticSkillDefinition.IsDefaultEnabled"/>; apps own a separate inventory).
/// Kept for JSON backward compatibility.
/// </summary>
public record PolicyPacksConfig
{
    [JsonPropertyName("enabledSkillIds")]
    public List<string>? EnabledSkillIds { get; init; }

    [JsonPropertyName("enabledGuardrailIds")]
    public List<string>? EnabledGuardrailIds { get; init; }

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
    /// <summary>skill | always_on | requestable — rules use always_on/requestable.</summary>
    public string Activation { get; init; } = AgenticSkillActivation.Skill;
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

public sealed record AgenticAppSkillDefinition
{
    public required string AppId { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string PromptMarkdown { get; init; } = string.Empty;
    public string Category { get; init; } = "general";
    public string Activation { get; init; } = AgenticSkillActivation.Skill;
    public bool IsEnabled { get; init; } = true;
    public int SortOrder { get; init; }
    public IReadOnlyList<string> LinkedGuardrailIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public AgenticSkillDefinition ToSkillDefinition() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            PromptMarkdown = PromptMarkdown,
            Category = Category,
            Activation = string.IsNullOrWhiteSpace(Activation) ? AgenticSkillActivation.Skill : Activation,
            IsSystem = false,
            IsDefaultEnabled = IsEnabled,
            SortOrder = SortOrder,
            LinkedGuardrailIds = LinkedGuardrailIds,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
}

public sealed record AgenticAppGuardrailDefinition
{
    public required string AppId { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Kind { get; init; }
    public string ConfigJson { get; init; } = "{}";
    public bool IsEnabled { get; init; } = true;
    public int SortOrder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public AgenticGuardrailDefinition ToGuardrailDefinition() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Kind = Kind,
            ConfigJson = ConfigJson,
            IsSystem = false,
            IsDefaultEnabled = IsEnabled,
            SortOrder = SortOrder,
            UpdatedAt = UpdatedAt
        };
}

public sealed record AgenticCatalogSnapshot
{
    public IReadOnlyList<AgenticSkillDefinition> Skills { get; init; } = [];
    public IReadOnlyList<AgenticGuardrailDefinition> Guardrails { get; init; } = [];
}

public sealed record AgenticAppCatalogSnapshot
{
    public IReadOnlyList<AgenticAppSkillDefinition> Skills { get; init; } = [];
    public IReadOnlyList<AgenticAppGuardrailDefinition> Guardrails { get; init; } = [];
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
    public const string PreToolUse = "pre-tool-use";
    public const string PostToolUse = "post-tool-use";
    public const string LiveDataEvidence = "live-data-evidence";
    /// <summary>
    /// Reject final answers that name internal tools or narrate/ask permission to call them
    /// instead of emitting tool_calls. End users should only see the result.
    /// </summary>
    public const string ToolSurfaceHidden = "tool-surface-hidden";

    // --- LLM Guardrails catalog (image) — default OFF in seed ---
    public const string InappropriateContent = "inappropriate-content";
    public const string OffensiveLanguage = "offensive-language";
    public const string PromptInjection = "prompt-injection";
    public const string SensitivePii = "sensitive-pii";
    public const string CompetitorMention = "competitor-mention";
    public const string PriceQuote = "price-quote";
    public const string SourceContext = "source-context";
    public const string Gibberish = "gibberish";
    public const string SqlQuery = "sql-query";
    public const string OpenApiResponse = "openapi-response";
    public const string JsonFormat = "json-format";
    public const string LogicalFlow = "logical-flow";
    public const string ResponseQuality = "response-quality";
    public const string TranslationAccuracy = "translation-accuracy";
    public const string DuplicateSentence = "duplicate-sentence";
    public const string Readability = "readability";
    public const string Relevance = "relevance";
    public const string PromptAddress = "prompt-address";
    public const string UrlAvailability = "url-availability";
    public const string FactCheck = "fact-check";
    /// <summary>
    /// Reject answers that cite specific numeric values (prices, dates, percentages, counts)
    /// without those values appearing in successful tool evidence.
    /// </summary>
    public const string NumericGrounding = "numeric-grounding";
}
