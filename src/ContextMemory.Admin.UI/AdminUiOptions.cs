namespace ContextMemory.Admin.UI;

public sealed class AdminUiOptions
{
    public const string SectionName = "Admin";

    /// <summary>Base URL used by the Admin server HttpClient (e.g. http://api:8080 in Docker).</summary>
    public string DefaultApiBaseUrl { get; set; } = "http://localhost:5100";

    /// <summary>URL shown to humans in the browser (e.g. http://localhost:5100).</summary>
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5100";

    /// <summary>Optional pre-filled master key for local/dev (never use in production).</summary>
    public string DefaultMasterKey { get; set; } = string.Empty;

    /// <summary>Folder with markdown docs (repo <c>docs/</c>). Env: Admin__DocsPath.</summary>
    public string DocsPath { get; set; } = string.Empty;
}
