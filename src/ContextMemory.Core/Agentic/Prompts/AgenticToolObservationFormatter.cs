using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic.Prompts;

public static class AgenticToolObservationFormatter
{
    public const int DefaultMaxObservationChars = 2_000;

    public static string Format(
        string toolName,
        ToolExecutionResult result,
        AppRuntimeConfig config,
        string? artifactId = null)
    {
        var payload = result.Summary ?? result.Output;
        if (result.Entities is { Count: > 0 })
        {
            var entityLine = string.Join(", ", result.Entities.Select(kv => $"{kv.Key}={kv.Value}"));
            payload = $"{payload}\nEntities: {entityLine}";
        }

        var maxChars = config.MaxToolObservationChars > 0
            ? config.MaxToolObservationChars
            : DefaultMaxObservationChars;
        payload = TruncateWithPointer(toolName, payload ?? string.Empty, maxChars, artifactId);

        return AgenticPromptProfileResolver.Resolve(config) switch
        {
            AgenticPromptProfile.OpenAi =>
                $"Function `{toolName}` returned (exit_code={result.ExitCode}):\n{payload}",
            AgenticPromptProfile.Claude =>
                $"Resultado da tool `{toolName}` (exit={result.ExitCode}):\n{payload}",
            _ =>
                $"[{toolName}] exit_code={result.ExitCode}\n{payload}"
        };
    }

    /// <summary>
    /// Cursor-style dynamic discovery: keep a short preview in context; full payload in artifact store.
    /// </summary>
    internal static string TruncateWithPointer(
        string toolName,
        string payload,
        int maxChars,
        string? artifactId = null)
    {
        var hash = payload.GetHashCode(StringComparison.Ordinal).ToString("x8");
        var id = string.IsNullOrWhiteSpace(artifactId)
            ? $"tool:{toolName}:{hash}"
            : artifactId.Trim();
        var pointer =
            $"\n\n…[artifactId={id} — use artifact_tail or artifact_read to load more]";

        if (maxChars <= 0 || payload.Length <= maxChars)
        {
            // Still attach pointer when an artefact was persisted (sandbox always-on).
            return string.IsNullOrWhiteSpace(artifactId) ? payload : payload + pointer;
        }

        // previewLen must never exceed payload.Length (maxChars can be < 64 + pointer overhead).
        var previewLen = Math.Clamp(maxChars - 120, 1, payload.Length);
        previewLen = Math.Min(previewLen, Math.Max(1, payload.Length - 1));
        var preview = payload[..previewLen];
        return preview
               + $"\n\n…[truncated {payload.Length - previewLen} chars; artifactId={id} "
               + "— use artifact_tail or artifact_read to load more]";
    }

    public static string BuildArtifactId(string toolName, string payload)
    {
        var hash = payload.GetHashCode(StringComparison.Ordinal).ToString("x8");
        return $"tool:{toolName}:{hash}";
    }
}
