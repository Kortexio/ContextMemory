using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticBrowserTools
{
    public const string Navigate = "browser_navigate";
    public const string Snapshot = "browser_snapshot";
    public const string Click = "browser_click";
    public const string Type = "browser_type";
    public const string Screenshot = "browser_screenshot";

    public static bool IsBrowserTool(string? toolName) =>
        string.Equals(toolName, Navigate, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, Snapshot, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, Click, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, Type, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, Screenshot, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig)
    {
        if (!runtimeConfig.Agentic.Tools.Browser.Enabled)
            return [];

        return
        [
            new OllamaTool("function", new OllamaFunction(
                Navigate,
                "Open a URL in the headless browser (allowlisted hosts only).",
                new
                {
                    type = "object",
                    properties = new { url = new { type = "string" } },
                    required = new[] { "url" }
                })),
            new OllamaTool("function", new OllamaFunction(
                Snapshot,
                "Return an accessibility/DOM summary of the current page (refs for click/type).",
                new { type = "object", properties = new { } })),
            new OllamaTool("function", new OllamaFunction(
                Click,
                "Click an element by ref from browser_snapshot.",
                new
                {
                    type = "object",
                    properties = new { @ref = new { type = "string", description = "Element ref from snapshot" } },
                    required = new[] { "ref" }
                })),
            new OllamaTool("function", new OllamaFunction(
                Type,
                "Type text into an element by ref from browser_snapshot.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        @ref = new { type = "string" },
                        text = new { type = "string" }
                    },
                    required = new[] { "ref", "text" }
                })),
            new OllamaTool("function", new OllamaFunction(
                Screenshot,
                "Capture a PNG screenshot; stores as session artifact. Use screenshot_describe when the model supports vision.",
                new { type = "object", properties = new { fullPage = new { type = "boolean" } } }))
        ];
    }
}
