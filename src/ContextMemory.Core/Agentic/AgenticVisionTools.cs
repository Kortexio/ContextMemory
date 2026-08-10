using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticVisionTools
{
    public const string ReadImage = "read_image";
    public const string ScreenshotDescribe = "screenshot_describe";
    public const string ImageBase64Entity = "imageBase64";

    public static bool IsVisionTool(string? toolName) =>
        string.Equals(toolName, ReadImage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, ScreenshotDescribe, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig, bool supportsVision)
    {
        var vision = runtimeConfig.Agentic.Tools.Vision;
        if (!vision.Enabled || !supportsVision)
            return [];

        return
        [
            new OllamaTool("function", new OllamaFunction(
                ReadImage,
                "Load an image from an allowlisted URL or session artifactId so the multimodal model can see it on the next turn.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "Allowlisted image URL (http/https or data:)" },
                        artifactId = new { type = "string", description = "Session artifact id holding base64 image bytes" }
                    }
                })),
            new OllamaTool("function", new OllamaFunction(
                ScreenshotDescribe,
                "Attach a browser screenshot artifact (PNG base64) for the multimodal model to describe on the next turn.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        artifactId = new { type = "string", description = "Artifact id from browser_screenshot" }
                    },
                    required = new[] { "artifactId" }
                }))
        ];
    }
}
