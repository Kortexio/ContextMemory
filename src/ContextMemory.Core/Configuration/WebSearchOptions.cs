namespace ContextMemory.Core.Configuration;

public class WebSearchOptions
{
    public string TavilyApiKey { get; set; } = string.Empty;
    public string BraveApiKey { get; set; } = string.Empty;
    public string DefaultProvider { get; set; } = "tavily";
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int ClassifierTimeoutSeconds { get; set; } = 10;
}
