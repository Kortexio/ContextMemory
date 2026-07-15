namespace ContextMemory.Core.Models;

public record AppProfile
{
    public required string AppId { get; init; }
    public required string ApiKey { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = "en-US";
    public int MaxHistoryMessages { get; init; } = 20;
    public bool IsActive { get; init; } = true;
}
