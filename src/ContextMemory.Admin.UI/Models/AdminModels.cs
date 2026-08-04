using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Models;

public static class AdminJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class AdminAppListItem
{
    public string AppId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public AppTelemetryDto? Stats { get; set; }
}

public sealed class AppTelemetryDto
{
    public long RequestsTotal { get; set; }
    public long RequestsError { get; set; }
    public long TokensPrompt { get; set; }
    public long TokensCompletion { get; set; }
    public double AvgLatencyMs { get; set; }
    public int ActiveUsers { get; set; }
    public int WikiContextChars { get; set; }
    public int WikiPagesIncluded { get; set; }
    public int WikiPagesTotal { get; set; }
    public long WikiTruncatedTotal { get; set; }
    public long WikiCompactionSuccess { get; set; }
    public long WikiCompactionErrors { get; set; }
    public long WebSearchTotal { get; set; }
    public long WebSearchHits { get; set; }
    public long WebSearchSkippedTotal { get; set; }
    public double WebSearchLastLatencyMs { get; set; }
}

public sealed class AppStatsResponse
{
    public string AppId { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public AppTelemetryDto? Telemetry { get; set; }
}

public sealed class AppCredentialsDto
{
    public string AppId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool RotationPersists { get; set; }
}

public sealed class AppRuntimeConfigDto
{
    public string AppId { get; set; } = string.Empty;
    public string BasePersona { get; set; } = string.Empty;
    public string BusinessRules { get; set; } = string.Empty;
    public string FormatRules { get; set; } = string.Empty;
    public string WikiSchema { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "en-US";
    public string LlmModel { get; set; } = string.Empty;
    public string LlmBackend { get; set; } = "ollama";
    public string LlmEndpoint { get; set; } = string.Empty;
    public string LlmApiKey { get; set; } = string.Empty;
    public int MaxHistoryMessages { get; set; }
    public int MaxWikiContextChars { get; set; }
    public long WikiCompactionThresholdBytes { get; set; }
    public int WikiCompactionMinPages { get; set; }
    public bool StreamingEnabled { get; set; }
    public bool GlobalWikiEnabled { get; set; } = true;
    public int MaxGlobalWikiToolChars { get; set; }
    public RateLimitConfig? RateLimits { get; set; }
    public WebSearchConfig? WebSearch { get; set; }
    public AgenticConfig? Agentic { get; set; }
}

public sealed class AgenticCatalogAdminDto
{
    public List<AgenticSkillAdminDto> Skills { get; set; } = [];
    public List<AgenticGuardrailAdminDto> Guardrails { get; set; } = [];
}

public sealed class AgenticSkillAdminDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptMarkdown { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public bool IsSystem { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; }
    public List<string> LinkedGuardrailIds { get; set; } = [];
}

public sealed class AgenticGuardrailAdminDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsSystem { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public int SortOrder { get; set; }
}

public sealed class AgenticAppCatalogAdminDto
{
    public List<AgenticAppSkillAdminDto> Skills { get; set; } = [];
    public List<AgenticAppGuardrailAdminDto> Guardrails { get; set; } = [];
}

public sealed class AgenticAppSkillAdminDto
{
    public string AppId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptMarkdown { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<string> LinkedGuardrailIds { get; set; } = [];
}

public sealed class AgenticAppGuardrailAdminDto
{
    public string AppId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class McpServerAdminDto
{
    public McpServerInfo? Server { get; set; }
    public McpCatalogSyncAdminDto? Sync { get; set; }
}

public sealed class McpCatalogSyncAdminDto
{
    public string AppId { get; set; } = string.Empty;
    public string IntegrationName { get; set; } = string.Empty;
    public int ToolCount { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class HealthResponseDto
{
    public string Status { get; set; } = string.Empty;
    public HealthChecksDto? Checks { get; set; }
}

public sealed class HealthChecksDto
{
    public string? Ollama { get; set; }
    public string? Database { get; set; }
    public string? Persistence { get; set; }
    public bool AppsLoaded { get; set; }
    public bool ProfilesReady { get; set; }
    public string? SessionsPath { get; set; }
    public string? DefaultModel { get; set; }
}

public sealed class AdminSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    public string MasterKey { get; set; } = string.Empty;
}

public sealed class AdminApiException : Exception
{
    public int StatusCode { get; }

    public AdminApiException(int statusCode, string message) : base(message) =>
        StatusCode = statusCode;
}

public sealed class RegisterAppForm
{
    [Required(ErrorMessage = "Application name is required.")]
    [StringLength(128, MinimumLength = 2)]
    public string AppName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Domain is required.")]
    [RegularExpression(@"^[a-zA-Z0-9-]+$", ErrorMessage = "Use letters, numbers, and hyphen only.")]
    [StringLength(64, MinimumLength = 2)]
    public string Domain { get; set; } = string.Empty;

    [Required]
    public string DefaultLanguage { get; set; } = "en-US";

    [Required]
    public string LlmBackend { get; set; } = "ollama";

    [Required]
    public string LlmModel { get; set; } = "qwen3.5:9b";

    public string? LlmEndpoint { get; set; }

    public string? LlmApiKey { get; set; }

    public string? PromptPersona { get; set; }

    public RegisterAppRequest ToRequest() => new()
    {
        AppName = AppName.Trim(),
        Domain = Domain.Trim().ToLowerInvariant(),
        DefaultLanguage = DefaultLanguage.Trim(),
        LlmBackend = LlmBackend.Trim(),
        LlmModel = LlmModel.Trim(),
        LlmEndpoint = string.IsNullOrWhiteSpace(LlmEndpoint) ? null : LlmEndpoint.Trim(),
        LlmApiKey = string.IsNullOrWhiteSpace(LlmApiKey) ? null : LlmApiKey.Trim(),
        PromptPersona = string.IsNullOrWhiteSpace(PromptPersona) ? string.Empty : PromptPersona.Trim()
    };
}
