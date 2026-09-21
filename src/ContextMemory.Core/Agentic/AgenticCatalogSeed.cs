using System.Text.Json;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Single bootstrap loader for the platform agentic catalog (skills + guardrails).
/// Admin DB is runtime truth; this only inserts missing rows on first startup.
/// </summary>
public static class AgenticCatalogSeed
{
    private static readonly Lazy<Catalog> Loaded = new(Load);

    public static IReadOnlyList<AgenticSkillDefinition> Skills => Loaded.Value.Skills;

    public static IReadOnlyList<AgenticGuardrailDefinition> Guardrails => Loaded.Value.Guardrails;

    private sealed record Catalog(
        IReadOnlyList<AgenticSkillDefinition> Skills,
        IReadOnlyList<AgenticGuardrailDefinition> Guardrails);

    private static Catalog Load()
    {
        const string resource = "ContextMemory.Core.Agentic.Seed.agentic-catalog.json";
        using var stream = typeof(AgenticCatalogSeed).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded seed '{resource}' is missing. Mark Agentic/Seed/agentic-catalog.json as EmbeddedResource.");

        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("skills", out var skillsEl) || skillsEl.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("guardrails", out var guardrailsEl) || guardrailsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("agentic-catalog.json must contain 'skills' and 'guardrails' arrays.");
        }

        var skills = skillsEl.EnumerateArray().Select(MapSkill).ToList();
        var guardrails = guardrailsEl.EnumerateArray().Select(MapGuardrail).ToList();
        if (skills.Count == 0 || guardrails.Count == 0)
            throw new InvalidOperationException("agentic-catalog.json skills/guardrails must be non-empty.");

        return new Catalog(skills, guardrails);
    }

    private static AgenticSkillDefinition MapSkill(JsonElement e)
    {
        var now = DateTimeOffset.UnixEpoch;
        return new AgenticSkillDefinition
        {
            Id = Req(e, "id"),
            Name = Req(e, "name"),
            Description = Str(e, "description"),
            PromptMarkdown = Str(e, "promptMarkdown"),
            Category = Str(e, "category", "general"),
            Activation = Str(e, "activation", AgenticSkillActivation.Skill),
            IsSystem = Bool(e, "isSystem", true),
            IsDefaultEnabled = Bool(e, "isDefaultEnabled", true),
            SortOrder = Int(e, "sortOrder"),
            LinkedGuardrailIds = Strings(e, "linkedGuardrailIds"),
            CreatedAt = Time(e, "createdAt", now),
            UpdatedAt = Time(e, "updatedAt", now)
        };
    }

    private static AgenticGuardrailDefinition MapGuardrail(JsonElement e) =>
        new()
        {
            Id = Req(e, "id"),
            Name = Req(e, "name"),
            Description = Str(e, "description"),
            Kind = Req(e, "kind"),
            ConfigJson = e.TryGetProperty("config", out var cfg)
                         && cfg.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null
                ? cfg.GetRawText()
                : "{}",
            IsSystem = Bool(e, "isSystem", true),
            IsDefaultEnabled = Bool(e, "isDefaultEnabled", true),
            SortOrder = Int(e, "sortOrder"),
            UpdatedAt = Time(e, "updatedAt", DateTimeOffset.UnixEpoch)
        };

    private static string Req(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : throw new InvalidOperationException($"Catalog seed entry missing '{name}'.");

    private static string Str(JsonElement e, string name, string fallback = "") =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? fallback).Trim()
            : fallback;

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    private static DateTimeOffset Time(JsonElement e, string name, DateTimeOffset fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), out var t)
            ? t
            : fallback;

    private static IReadOnlyList<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(x => x.GetString()?.Trim() ?? "").Where(x => x.Length > 0).ToList()
            : [];
}
