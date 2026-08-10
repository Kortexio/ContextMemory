namespace ContextMemory.Core.Configuration;

public class ContextMemoryOptions
{
    public const string SectionName = "ContextMemory";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string DataPath { get; set; } = "./data";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public int OllamaRequestTimeoutSeconds { get; set; } = 600;
    public int DefaultAgenticLoopTimeoutSeconds { get; set; } = 120;
    public int MaxHistoryMessages { get; set; } = 6;

    /// <summary>Session wiki inject budget (tokens-first; dynamic context discovery).</summary>
    public int MaxWikiContextChars { get; set; } = 4_000;

    /// <summary>Global Wiki digest inject budget in the system prompt (not full document bodies).</summary>
    public int MaxDigestContextChars { get; set; } = 2_500;

    /// <summary>How many digest matches to inject before the chat LLM runs.</summary>
    public int DigestTopK { get; set; } = 3;

    /// <summary>Cap for tool observations in the agent loop; excess becomes a pointer + preview.</summary>
    public int MaxToolObservationChars { get; set; } = 2_000;

    /// <summary>Estimated tokens before mid-turn compaction archives the transcript.</summary>
    public int MaxContextTokens { get; set; } = 24_000;

    /// <summary>Fixed preview size for sandbox/terminal observations (always artefact).</summary>
    public int SandboxObservationPreviewChars { get; set; } = 400;
    public long WikiCompactionThresholdBytes { get; set; } = 524_288;
    public int WikiCompactionMinPages { get; set; } = 8;
    public int MaxPayloadBytes { get; set; } = 1_048_576;
    public string MasterKey { get; set; } = string.Empty;
    public string LmStudioEndpoint { get; set; } = "http://localhost:1234";
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public bool EnableMetrics { get; set; } = true;
    public bool AdminEnabled { get; set; } = true;
    public int DefaultRateLimitRpm { get; set; } = 60;
    public int DefaultRateLimitTpm { get; set; } = 100_000;
    public int ActiveUserWindowMinutes { get; set; } = 15;
    public List<string> AdminCorsOrigins { get; set; } = [];
    public string PersistenceProvider { get; set; } = "File";
    public string DefaultLlmModel { get; set; } = "qwen3.5:9b";

    /// <summary>
    /// Platform default model for session wiki maintainer/compactor when an app does not set <c>WikiLlmModel</c>.
    /// Empty = fall back to the app chat model.
    /// </summary>
    public string DefaultWikiLlmModel { get; set; } = string.Empty;

    /// <summary>
    /// Optional HTTP base URL of the MCP stdio runtime sidecar (e.g. http://mcp-runtime:8080).
    /// When set, stdio MCP servers are executed in the sidecar instead of the API process.
    /// </summary>
    public string McpRuntimeUrl { get; set; } = string.Empty;
    public Dictionary<string, AppOptionsEntry> Apps { get; set; } = new();
    public WebSearchOptions WebSearch { get; set; } = new();
}

public class AppOptionsEntry
{
    public string ApiKey { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "en-US";
    public string LlmModel { get; set; } = "qwen3.5:9b";
    public int MaxHistoryMessages { get; set; } = 6;
}
