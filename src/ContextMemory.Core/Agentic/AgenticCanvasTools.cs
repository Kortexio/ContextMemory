using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticCanvasTools
{
    public const string CanvasWrite = "canvas_write";
    public const string CanvasRead = "canvas_read";
    public const string MainArtifactId = "canvas:main";

    public static bool IsCanvasTool(string? toolName) =>
        string.Equals(toolName, CanvasWrite, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, CanvasRead, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig)
    {
        if (!runtimeConfig.Agentic.Tools.Canvas.Enabled)
            return [];

        return
        [
            new OllamaTool("function", new OllamaFunction(
                CanvasWrite,
                "Write/replace the session Canvas document shown beside Admin Chat Lab. JSON: {version, title, sections:[{heading, markdown|table|mermaid|metrics}]}",
                new
                {
                    type = "object",
                    properties = new
                    {
                        canvas = new { type = "object", description = "Canvas document object" },
                        json = new { type = "string", description = "Alternative: canvas as JSON string" }
                    }
                })),
            new OllamaTool("function", new OllamaFunction(
                CanvasRead,
                "Read the current session Canvas document.",
                new { type = "object", properties = new { } }))
        ];
    }
}
