using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticHttpTools
{
    public const string FetchUrl = "fetch_url";
    public const string HttpRequest = "http_request";
    public const string WebSearch = "web_search";

    public static bool IsHttpTool(string? toolName) =>
        string.Equals(toolName, FetchUrl, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, HttpRequest, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, WebSearch, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig)
    {
        var http = runtimeConfig.Agentic.Tools.Http;
        if (!http.Enabled)
            return [];

        var tools = new List<OllamaTool>
        {
            new("function", new OllamaFunction(
                FetchUrl,
                "HTTP GET a public URL from the allowlist. Returns status, content-type, and truncated text body. Prefer MCP for authenticated APIs.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "Absolute http/https URL" },
                        maxChars = new { type = "integer", description = "Max response characters (optional)" }
                    },
                    required = new[] { "url" }
                }))
        };

        if (http.AllowHttpRequest)
        {
            tools.Add(new OllamaTool("function", new OllamaFunction(
                HttpRequest,
                "HTTP request (GET/POST/PUT/PATCH/DELETE) to an allowlisted host. Do not invent OAuth — use MCP for Zuora and authenticated APIs.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        method = new { type = "string", description = "GET|POST|PUT|PATCH|DELETE" },
                        url = new { type = "string", description = "Absolute http/https URL" },
                        headers = new { type = "object", description = "Optional request headers" },
                        body = new { type = "string", description = "Optional request body" },
                        maxChars = new { type = "integer", description = "Max response characters (optional)" }
                    },
                    required = new[] { "method", "url" }
                })));
        }

        if (http.AllowWebSearchTool)
        {
            tools.Add(new OllamaTool("function", new OllamaFunction(
                WebSearch,
                "Search the public web via the tenant search provider (Tavily/Brave). Use for freshness; cite [web] + URL.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search query" },
                        maxResults = new { type = "integer", description = "Max hits (default from tenant webSearch)" }
                    },
                    required = new[] { "query" }
                })));
        }

        return tools;
    }
}
