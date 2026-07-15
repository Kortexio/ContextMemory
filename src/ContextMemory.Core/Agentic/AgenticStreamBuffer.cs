using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

/// <summary>
/// Emits text chunks only after the orchestrator has the final response —
/// tool calls and internal observations are never exposed to the client.
/// </summary>
public static class AgenticStreamBuffer
{
    public const int DefaultChunkSize = 32;

    public static IEnumerable<OllamaResponse> Stream(
        string model,
        string finalAnswer,
        int chunkSize = DefaultChunkSize)
    {
        if (string.IsNullOrEmpty(finalAnswer))
        {
            yield return new OllamaResponse
            {
                Model = model,
                Message = new OllamaMessage { Role = "assistant", Content = string.Empty },
                Done = true
            };
            yield break;
        }

        for (var i = 0; i < finalAnswer.Length; i += chunkSize)
        {
            var slice = finalAnswer[i..Math.Min(i + chunkSize, finalAnswer.Length)];
            yield return new OllamaResponse
            {
                Model = model,
                Message = new OllamaMessage { Role = "assistant", Content = slice },
                Done = false
            };
        }

        yield return new OllamaResponse
        {
            Model = model,
            Message = new OllamaMessage { Role = "assistant", Content = string.Empty },
            Done = true
        };
    }
}
