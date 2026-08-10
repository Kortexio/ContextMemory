using System.Text;
using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.GlobalWiki;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class DocumentToolsExecutor : ISessionScopedToolExecutor
{
    private readonly ISessionArtifactStore _artifacts;
    private readonly GlobalWikiService _wiki;
    private readonly ILogger<DocumentToolsExecutor> _logger;

    public DocumentToolsExecutor(
        ISessionArtifactStore artifacts,
        GlobalWikiService wiki,
        ILogger<DocumentToolsExecutor> logger)
    {
        _artifacts = artifacts;
        _wiki = wiki;
        _logger = logger;
    }

    public bool CanExecute(string toolName, AppRuntimeConfig runtimeConfig) =>
        runtimeConfig.Agentic.Tools.Documents.Enabled
        && AgenticDocumentTools.IsDocumentTool(toolName);

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
                Output = "Document tools are disabled.",
                ExitCode = 1
            };
        }

        string? artifactId = null;
        bool? persistOverride = null;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
            var root = doc.RootElement;
            if (root.TryGetProperty("artifactId", out var a))
                artifactId = a.GetString();
            if (root.TryGetProperty("persistToWiki", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False)
                persistOverride = p.GetBoolean();
        }
        catch
        {
            return new ToolExecutionResult { Output = "Invalid parse_pdf arguments.", ExitCode = 1 };
        }

        if (string.IsNullOrWhiteSpace(artifactId))
            return new ToolExecutionResult { Output = "artifactId is required.", ExitCode = 1 };

        var raw = await _artifacts
            .ReadAsync(appId, userId, sessionId, artifactId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return new ToolExecutionResult { Output = $"Artifact '{artifactId}' not found.", ExitCode = 1 };

        byte[] bytes;
        try
        {
            var payload = raw.Trim();
            if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = payload.IndexOf(',');
                payload = comma >= 0 ? payload[(comma + 1)..] : payload;
            }

            bytes = Convert.FromBase64String(payload);
        }
        catch
        {
            // Treat stored content as already-extracted text
            return TruncateResult(raw, runtimeConfig.Agentic.Tools.Documents.MaxExtractChars);
        }

        var docs = runtimeConfig.Agentic.Tools.Documents;
        if (docs.MaxBytes > 0 && bytes.Length > docs.MaxBytes)
        {
            return new ToolExecutionResult
            {
                Output = $"PDF exceeds maxBytes ({bytes.Length} > {docs.MaxBytes}).",
                ExitCode = 1
            };
        }

        string text;
        try
        {
            text = ExtractPdfText(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF extract failed for {ArtifactId}", artifactId);
            return new ToolExecutionResult
            {
                Output = $"PDF extract failed: {ex.Message}",
                ExitCode = 1
            };
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ToolExecutionResult
            {
                Output =
                    "No text layer found in PDF (likely a scan). "
                    + (LlmCapabilitiesGate.SupportsVision(runtimeConfig)
                        ? "Use browser_screenshot / read_image with a vision model."
                        : "OCR is not available for this text-only model."),
                ExitCode = 1
            };
        }

        var maxChars = docs.MaxExtractChars > 0 ? docs.MaxExtractChars : 100_000;
        var truncated = text.Length > maxChars;
        if (truncated)
            text = text[..maxChars] + $"\n…[truncated]";

        var extractId = $"pdf-extract:{Guid.NewGuid():N}"[..24];
        await _artifacts.WriteAsync(appId, userId, sessionId, extractId, text, cancellationToken)
            .ConfigureAwait(false);

        var persist = persistOverride ?? docs.PersistToWiki;
        if (persist && runtimeConfig.GlobalWikiEnabled)
        {
            try
            {
                await _wiki.UpsertAsync(
                        appId,
                        $"pdf-{artifactId}",
                        new GlobalWikiUpsertRequest
                        {
                            Title = $"PDF extract {artifactId}",
                            Content = text,
                            SourceId = "pdf-ingest",
                            Metadata = new Dictionary<string, string>
                            {
                                ["kind"] = "pdf",
                                ["sourceArtifactId"] = artifactId
                            }
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist PDF extract to wiki");
            }
        }

        return new ToolExecutionResult
        {
            Output = text,
            ExitCode = 0,
            OutputTruncated = truncated,
            Entities = new Dictionary<string, string> { ["artifactId"] = extractId }
        };
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static ToolExecutionResult TruncateResult(string text, int maxChars)
    {
        maxChars = maxChars > 0 ? maxChars : 100_000;
        var truncated = text.Length > maxChars;
        return new ToolExecutionResult
        {
            Output = truncated ? text[..maxChars] + "\n…[truncated]" : text,
            ExitCode = 0,
            OutputTruncated = truncated
        };
    }
}
