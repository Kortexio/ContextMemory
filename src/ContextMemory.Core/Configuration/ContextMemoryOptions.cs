namespace ContextMemory.Core.Configuration;

public class ContextMemoryOptions
{
    public const string SectionName = "ContextMemory";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string DataPath { get; set; } = "./data";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public int OllamaRequestTimeoutSeconds { get; set; } = 600;
    public int DefaultAgenticLoopTimeoutSeconds { get; set; } = 120;
    public int MaxHistoryMessages { get; set; } = 20;
    public int MaxWikiContextChars { get; set; } = 12_000;
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
    public Dictionary<string, AppOptionsEntry> Apps { get; set; } = new();
    public WebSearchOptions WebSearch { get; set; } = new();
}

public class AppOptionsEntry
{
    public string ApiKey { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "en-US";
    public string LlmModel { get; set; } = "qwen3.5:9b";
    public int MaxHistoryMessages { get; set; } = 20;
}
