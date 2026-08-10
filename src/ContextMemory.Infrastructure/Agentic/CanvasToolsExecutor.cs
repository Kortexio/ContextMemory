using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class CanvasToolsExecutor : ISessionScopedToolExecutor
{
    private readonly ISessionArtifactStore _artifacts;

    public CanvasToolsExecutor(ISessionArtifactStore artifacts) => _artifacts = artifacts;

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Canvas.Enabled
        && AgenticCanvasTools.IsCanvasTool(toolName);

    public async Task<ToolExecutionResult> ExecuteAsync(
        OllamaToolCall toolCall,
        string appId,
        string userId,
        string sessionId,
        AppRuntimeConfig runtimeConfig,
        Action<AgenticProgressEvent>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(toolCall.Function.Name, runtimeConfig))
        {
            return new ToolExecutionResult
            {
                Output = "Canvas tools are disabled.",
                ExitCode = 1
            };
        }

        if (string.Equals(toolCall.Function.Name, AgenticCanvasTools.CanvasRead, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _artifacts
                .ReadAsync(appId, userId, sessionId, AgenticCanvasTools.MainArtifactId, cancellationToken)
                .ConfigureAwait(false);
            return new ToolExecutionResult
            {
                Output = string.IsNullOrWhiteSpace(existing) ? "(empty canvas)" : existing,
                ExitCode = 0
            };
        }

        string json;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            if (root.TryGetProperty("json", out var j) && j.ValueKind == JsonValueKind.String)
            {
                json = j.GetString() ?? "{}";
            }
            else if (root.TryGetProperty("canvas", out var c))
            {
                json = c.GetRawText();
            }
            else
            {
                // Treat whole args as canvas document
                json = root.GetRawText();
            }

            // Validate JSON
            using var _ = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                Output = $"Invalid canvas JSON: {ex.Message}",
                ExitCode = 1
            };
        }

        // Ensure version field for Admin renderer
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && !doc.RootElement.TryGetProperty("version", out _))
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", 1);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        prop.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        catch
        {
            // keep original json
        }

        await _artifacts
            .WriteAsync(appId, userId, sessionId, AgenticCanvasTools.MainArtifactId, json, cancellationToken)
            .ConfigureAwait(false);

        return new ToolExecutionResult
        {
            Output = json,
            ExitCode = 0,
            Summary = $"Canvas updated ({AgenticCanvasTools.MainArtifactId}).",
            Entities = new Dictionary<string, string> { ["artifactId"] = AgenticCanvasTools.MainArtifactId }
        };
    }
}
