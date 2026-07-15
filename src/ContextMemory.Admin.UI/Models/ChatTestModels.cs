namespace ContextMemory.Admin.UI.Models;

public enum ChatEndpointMode
{
    Chat,
    Generate
}

public sealed class ChatTestSettings
{
    public string AppId { get; set; } = "demo-dev";
    public string UserId { get; set; } = "admin-chat-test";
    public string SessionId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen3.5:9b";
    public ChatEndpointMode Mode { get; set; } = ChatEndpointMode.Chat;
    public bool Stream { get; set; }
    public string? Format { get; set; }
    public string? KeepAlive { get; set; } = "5m";
    public string? SystemPrompt { get; set; }
    public float? Temperature { get; set; } = 0.7f;
    public float? TopP { get; set; } = 0.9f;
    public int? TopK { get; set; } = 40;
    public int? NumCtx { get; set; } = 4096;
    public float? RepeatPenalty { get; set; } = 1.1f;
    public int? NumPredict { get; set; }
    public bool ShowRawJson { get; set; } = true;
}

public sealed class ChatUiMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Role { get; init; }
    public string Content { get; set; } = string.Empty;
    public string FinalContent { get; set; } = string.Empty;
    public List<AgenticUiStep> AgenticSteps { get; set; } = [];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public long? ElapsedMs { get; set; }
    public string? MessageId { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsError { get; set; }
    public int? PromptEvalCount { get; set; }
    public int? EvalCount { get; set; }
    public long? TotalDurationNs { get; set; }
    public bool HasAgenticProgress => AgenticSteps.Count > 0;
    public bool AwaitingConfirmation { get; set; }
    public string? PendingConfirmationId { get; set; }
}

public sealed class AgenticUiStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public int? Iteration { get; set; }
    public string? ToolName { get; set; }
}

public sealed class ChatExchangeResult
{
    public required string Content { get; init; }
    public string? MessageId { get; init; }
    public string? SessionId { get; init; }
    public long ElapsedMs { get; init; }
    public string? ResponseTimeHeaderMs { get; init; }
    public string? WebSearchUsed { get; init; }
    public string? WebSearchProvider { get; init; }
    public string? WebSearchSkipReason { get; init; }
    public string? RawRequestJson { get; init; }
    public string? RawResponseJson { get; init; }
    public OllamaResponseMeta? Meta { get; init; }
    public List<AgenticUiStep> AgenticSteps { get; init; } = [];
    public string FinalContent { get; init; } = string.Empty;
    public bool AwaitingConfirmation { get; init; }
    public string? PendingConfirmationId { get; init; }
    public bool IsError { get; init; }
    public int StatusCode { get; init; } = 200;
}

public sealed class OllamaResponseMeta
{
    public string? Model { get; init; }
    public string? DoneReason { get; init; }
    public int? PromptEvalCount { get; init; }
    public int? EvalCount { get; init; }
    public long? TotalDuration { get; init; }
    public long? EvalDuration { get; init; }
}

public sealed class AppDetailDto
{
    public string AppId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? AppName { get; set; }
    public string? Domain { get; set; }
    public string DefaultLanguage { get; set; } = "en-US";
    public string LlmBackend { get; set; } = "ollama";
    public string LlmModel { get; set; } = string.Empty;
    public bool StreamingEnabled { get; set; }
    public int MaxHistoryMessages { get; set; }
    public int ActiveUsers { get; set; }
}
