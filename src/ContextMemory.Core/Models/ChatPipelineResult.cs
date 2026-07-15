namespace ContextMemory.Core.Models;

public sealed class ChatPipelineResult
{
    public OllamaResponse? Response { get; init; }
    public string? MessageId { get; init; }
    public int EstimatedPromptTokens { get; init; }
    public int EstimatedCompletionTokens { get; init; }
}
