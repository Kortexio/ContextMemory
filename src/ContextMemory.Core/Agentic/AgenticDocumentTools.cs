using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticDocumentTools
{
    public const string ParsePdf = "parse_pdf";
    public const string ReadDocument = "read_document";

    public static bool IsDocumentTool(string? toolName) =>
        string.Equals(toolName, ParsePdf, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, ReadDocument, StringComparison.OrdinalIgnoreCase);

    public static List<OllamaTool> BuildTools(AppRuntimeConfig runtimeConfig)
    {
        if (!runtimeConfig.Agentic.Tools.Documents.Enabled)
            return [];

        return
        [
            new OllamaTool("function", new OllamaFunction(
                ParsePdf,
                "Extract text from a PDF session artifact (base64 or previously uploaded). Scanned PDFs without text layer cannot be OCR'd here.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        artifactId = new { type = "string", description = "Session artifact id containing PDF bytes (base64) or prior extract" },
                        persistToWiki = new { type = "boolean", description = "Optional override to write extract to global wiki" }
                    },
                    required = new[] { "artifactId" }
                })),
            new OllamaTool("function", new OllamaFunction(
                ReadDocument,
                "Alias of parse_pdf for document artifacts.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        artifactId = new { type = "string" }
                    },
                    required = new[] { "artifactId" }
                }))
        ];
    }
}
