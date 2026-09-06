using System.Text.Json.Serialization;

namespace ContextMemory.Core.Session;

/// <summary>
/// Importance tiers for memory / context items (CM-2).
/// </summary>
public enum MemoryImportance
{
    Critical = 0,
    Important = 1,
    Recoverable = 2,
    Discardable = 3
}

/// <summary>
/// Explicit working memory for the current task (CM-2).
/// </summary>
public sealed class WorkingMemory
{
    public string? Objective { get; set; }
    public string? Plan { get; set; }
    public List<string> RecentTools { get; set; } = [];
    public List<string> Blockers { get; set; } = [];
    public List<WorkingMemoryItem> Items { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void RecordTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return;
        RecentTools.RemoveAll(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase));
        RecentTools.Insert(0, toolName);
        if (RecentTools.Count > 12)
            RecentTools.RemoveRange(12, RecentTools.Count - 12);
        Touch();
    }

    public string ToPromptSection(int maxChars = 2000)
    {
        var lines = new List<string> { "## Working memory" };
        if (!string.IsNullOrWhiteSpace(Objective))
            lines.Add($"- Objective: {Objective.Trim()}");
        if (!string.IsNullOrWhiteSpace(Plan))
            lines.Add($"- Plan: {Plan.Trim()}");
        if (RecentTools.Count > 0)
            lines.Add($"- Recent tools: {string.Join(", ", RecentTools.Take(8))}");
        if (Blockers.Count > 0)
            lines.Add($"- Blockers: {string.Join("; ", Blockers.Take(5))}");

        foreach (var item in Items
                     .OrderBy(i => i.Importance)
                     .ThenByDescending(i => i.UpdatedAt)
                     .Take(12))
        {
            lines.Add($"- [{item.Importance}] {item.Key}: {Truncate(item.Value, 200)}");
        }

        var text = string.Join('\n', lines);
        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value
        : value[..max] + "…";
}

public sealed class WorkingMemoryItem
{
    public required string Key { get; init; }
    public required string Value { get; set; }
    public MemoryImportance Importance { get; set; } = MemoryImportance.Important;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsPreservedOnCompaction =>
        Importance is MemoryImportance.Critical or MemoryImportance.Important;
}
