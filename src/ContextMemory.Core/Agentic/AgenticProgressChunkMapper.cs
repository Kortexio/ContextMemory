using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticProgressChunkMapper
{
    public static OllamaResponse ToProgressChunk(string model, AgenticProgressEvent evt, string? defaultLanguage = null) =>
        new()
        {
            Model = model,
            Done = false,
            ContextMemory = new ContextMemoryMetadata
            {
                Agentic = AgenticStreamMetadata.FromProgress(evt, defaultLanguage)
            }
        };
}
