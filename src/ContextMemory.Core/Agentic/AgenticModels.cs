using System.Text.Json.Serialization;

namespace ContextMemory.Core.Agentic;

public record AgenticConfig
{
    public static AgenticConfig Disabled { get; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("tools")]
    public AgenticToolsConfig Tools { get; init; } = new();

    [JsonPropertyName("guardrails")]
    public AgenticGuardrailsConfig Guardrails { get; init; } = new();

    [JsonPropertyName("promptProfile")]
    public string PromptProfile { get; init; } = "auto";

    public bool HasExecutionTools => Tools.Execution.Count > 0;

    public bool HasIntegrationTools => Tools.Integrations.Count > 0;

    public bool HasAnyTools => HasExecutionTools || HasIntegrationTools;

    public int MaxIterations =>
        Guardrails.MaxIterations > 0 ? Guardrails.MaxIterations : 15;
}

public record AgenticToolsConfig
{
    [JsonPropertyName("execution")]
    public List<ExecutionToolConfig> Execution { get; init; } = [];

    [JsonPropertyName("integrations")]
    public List<IntegrationToolConfig> Integrations { get; init; } = [];
}

public record ExecutionToolConfig
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "aca-session";

    [JsonPropertyName("runtime")]
    public string Runtime { get; init; } = "shell";

    [JsonPropertyName("poolEndpoint")]
    public string? PoolEndpoint { get; init; }

    [JsonPropertyName("sandboxEndpoint")]
    public string? SandboxEndpoint { get; init; }

    [JsonPropertyName("allowEgress")]
    public bool AllowEgress { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("containerImage")]
    public string? ContainerImage { get; init; }
}

public record IntegrationToolConfig
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "mcp";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("authMode")]
    public string AuthMode { get; init; } = string.Empty;

    [JsonPropertyName("authToken")]
    public string? AuthToken { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }

    [JsonPropertyName("allowEgress")]
    public bool AllowEgress { get; init; }

    [JsonPropertyName("oauth")]
    public McpOAuthConfig? OAuth { get; init; }
}

public record McpOAuthConfig
{
    [JsonPropertyName("tokenUrl")]
    public string TokenUrl { get; init; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("clientSecret")]
    public string ClientSecret { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("audience")]
    public string? Audience { get; init; }
}

public record AgenticGuardrailsConfig
{
    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; init; } = 15;

    [JsonPropertyName("requireConfirmationFor")]
    public List<string> RequireConfirmationFor { get; init; } = [];

    [JsonPropertyName("networkEgress")]
    public string NetworkEgress { get; init; } = "restricted";

    [JsonPropertyName("loopTimeoutSeconds")]
    public int LoopTimeoutSeconds { get; init; }

    [JsonPropertyName("validationMode")]
    public string ValidationMode { get; init; } = "hybrid";

    [JsonPropertyName("minAnswerLength")]
    public int MinAnswerLength { get; init; }

    [JsonPropertyName("blockedAnswerPatterns")]
    public List<string> BlockedAnswerPatterns { get; init; } = [];

    [JsonPropertyName("allowedEgressHosts")]
    public List<string> AllowedEgressHosts { get; init; } = [];

    [JsonPropertyName("requireZeroExitCode")]
    public bool RequireZeroExitCode { get; init; } = true;

    [JsonPropertyName("expectedAnswerPatterns")]
    public List<string> ExpectedAnswerPatterns { get; init; } = [];

    [JsonPropertyName("humanReviewOnMaxIterations")]
    public bool HumanReviewOnMaxIterations { get; init; } = true;
}

public sealed class AgentResult
{
    public required string FinalAnswer { get; init; }
    public IReadOnlyList<AgentExecutionStep> Steps { get; init; } = [];
    public int Iterations { get; init; }
    public bool MaxIterationsReached { get; init; }
    public bool TimedOut { get; init; }
    public bool Success { get; init; }
    public bool AwaitingConfirmation { get; init; }
    public string? PendingConfirmationId { get; init; }
    public string? PendingKind { get; init; }

    public static AgentResult Succeeded(string answer, IReadOnlyList<AgentExecutionStep> steps, int iterations) =>
        new()
        {
            FinalAnswer = answer,
            Steps = steps,
            Iterations = iterations,
            Success = true
        };

    public static AgentResult LimitReached(string bestAnswer, IReadOnlyList<AgentExecutionStep> steps, int iterations) =>
        new()
        {
            FinalAnswer = bestAnswer,
            Steps = steps,
            Iterations = iterations,
            MaxIterationsReached = true,
            Success = false
        };

    public static AgentResult TimedOutPartial(string bestAnswer, IReadOnlyList<AgentExecutionStep> steps, int iterations) =>
        new()
        {
            FinalAnswer = bestAnswer,
            Steps = steps,
            Iterations = iterations,
            TimedOut = true,
            Success = false
        };

    public static AgentResult AwaitingHumanConfirmation(
        string message,
        string pendingId,
        IReadOnlyList<AgentExecutionStep> steps,
        int iterations,
        string? pendingKind = null) =>
        new()
        {
            FinalAnswer = message,
            Steps = steps,
            Iterations = iterations,
            AwaitingConfirmation = true,
            PendingConfirmationId = pendingId,
            PendingKind = pendingKind ?? AgenticPendingKinds.Destructive,
            Success = false
        };
}

public sealed class AgentExecutionStep
{
    public required int Iteration { get; init; }
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public required string Output { get; init; }
    public int? ExitCode { get; init; }
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public string? FeedbackForModel { get; init; }

    public static ValidationResult Ok() => new() { IsValid = true };

    public static ValidationResult Reject(string feedback) =>
        new() { IsValid = false, FeedbackForModel = feedback };
}

public sealed class ToolExecutionResult
{
    public required string Output { get; init; }
    public int ExitCode { get; init; }
    public bool Success => ExitCode == 0;
}
